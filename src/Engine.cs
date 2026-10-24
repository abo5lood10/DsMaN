using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;


namespace PotatoMixer
{
    // ------------------------------------------------------------ Core Audio COM interop (original declarations)
    [StructLayout(LayoutKind.Sequential)] struct PROPERTYKEY { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Sequential)] struct PROPVARIANT { public ushort vt, r1, r2, r3; public IntPtr ptr, ptr2; }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devs);
        void GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice dev);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice dev);
        void RegisterEndpointNotificationCallback(IntPtr p);
        void UnregisterEndpointNotificationCallback(IntPtr p);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { void GetCount(out int n); void Item(int i, out IMMDevice dev); }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        void Activate(ref Guid iid, int ctx, IntPtr p, [MarshalAs(UnmanagedType.IUnknown)] out object o);
        void OpenPropertyStore(int access, out IPropertyStore ps);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out int s);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out int c); void GetAt(int i, out PROPERTYKEY k);
        void GetValue(ref PROPERTYKEY k, out PROPVARIANT v); void SetValue(ref PROPERTYKEY k, ref PROPVARIANT v); void Commit();
    }
    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioClient
    {
        void Initialize(int shareMode, int flags, long bufDur, long period, IntPtr fmt, ref Guid session);
        void GetBufferSize(out uint n); void GetStreamLatency(out long l); void GetCurrentPadding(out uint p);
        void IsFormatSupported(int mode, IntPtr fmt, out IntPtr closest); void GetMixFormat(out IntPtr fmt);
        void GetDevicePeriod(out long def, out long min); void Start(); void Stop(); void Reset(); void SetEventHandle(IntPtr h);
        void GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object o);
    }
    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioCaptureClient
    {
        void GetBuffer(out IntPtr data, out int frames, out int flags, out long devPos, out long qpc);
        void ReleaseBuffer(int frames); void GetNextPacketSize(out int n);
    }
    [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioRenderClient { void GetBuffer(int frames, out IntPtr data); void ReleaseBuffer(int frames, int flags); }

    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceCompletionHandler { void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op); }
    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IActivateAudioInterfaceAsyncOperation { void GetActivateResult(out int hr, [MarshalAs(UnmanagedType.IUnknown)] out object iface); }
    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAgileObject { }

    [ClassInterface(ClassInterfaceType.None)]
    class ActivateHandler : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public ManualResetEvent Done = new ManualResetEvent(false); public int Hr; public object Iface;
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation op)
        {
            try { op.GetActivateResult(out Hr, out Iface); } catch (Exception) { Hr = -1; }
            Done.Set();
        }
    }

    // Spectral noise suppressor: 512-point sine-window STFT (50% overlap), adaptive per-bin noise floor,
    // Wiener-style gain. Strength 0..10 sets how hard the noise is cut (about -8 dB up to -30 dB).
    class Denoiser
    {
        const int N = 512, H = 256, B = N / 2 + 1;
        static readonly double[] win = MakeWin();
        static double[] MakeWin() { double[] w = new double[N]; for (int i = 0; i < N; i++) w[i] = Math.Sin(Math.PI * (i + 0.5) / N); return w; }
        class Chan
        {
            public List<float> inq = new List<float>(); public float[] ola = new float[N]; public Queue<float> outq = new Queue<float>();
            public double[] P = new double[B], noise = new double[B], gPrev = new double[B]; public bool init;
        }
        Chan[] ch = { new Chan(), new Chan() }; double[] re = new double[N], im = new double[N];
        public Denoiser() { foreach (Chan c in ch) for (int i = 0; i < N + H; i++) c.outq.Enqueue(0f); }

        public void Process(float[] b, int n, float k)
        {
            double alpha = 1.0 + 0.35 * k, floor = Math.Pow(10, -(8.0 + 2.2 * k) / 20.0);
            for (int c = 0; c < 2; c++)
            {
                Chan s = ch[c];
                for (int i = 0; i < n; i++) s.inq.Add(b[2 * i + c]);
                while (s.inq.Count >= N)
                {
                    for (int i = 0; i < N; i++) { re[i] = s.inq[i] * win[i]; im[i] = 0; }
                    Fft(re, im, false);
                    for (int kb = 0; kb < B; kb++)
                    {
                        double pw = re[kb] * re[kb] + im[kb] * im[kb];
                        if (!s.init) { s.P[kb] = pw; s.noise[kb] = pw; }
                        s.P[kb] = 0.85 * s.P[kb] + 0.15 * pw;
                        if (s.P[kb] < s.noise[kb]) s.noise[kb] = s.P[kb]; else s.noise[kb] *= 1.0015;
                        double g = 1.0 - alpha * s.noise[kb] / (s.P[kb] + 1e-12);
                        if (g < floor) g = floor; if (g > 1) g = 1;
                        g = 0.5 * s.gPrev[kb] + 0.5 * g; s.gPrev[kb] = g;
                        re[kb] *= g; im[kb] *= g;
                        if (kb > 0 && kb < N / 2) { re[N - kb] = re[kb]; im[N - kb] = -im[kb]; }
                    }
                    s.init = true;
                    Fft(re, im, true);
                    for (int i = 0; i < N; i++) s.ola[i] += (float)(re[i] / N * win[i]);
                    for (int i = 0; i < H; i++) s.outq.Enqueue(s.ola[i]);
                    Array.Copy(s.ola, H, s.ola, 0, N - H); Array.Clear(s.ola, N - H, H);
                    s.inq.RemoveRange(0, H);
                }
                for (int i = 0; i < n; i++) b[2 * i + c] = s.outq.Count > 0 ? s.outq.Dequeue() : 0f;
            }
        }

        static void Fft(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1; for (; (j & bit) != 0; bit >>= 1) j ^= bit; j ^= bit;
                if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
            }
            for (int len = 2; len <= n; len <<= 1)
            {
                double ang = 2 * Math.PI / len * (inverse ? 1 : -1), wr = Math.Cos(ang), wi = Math.Sin(ang);
                for (int i = 0; i < n; i += len)
                {
                    double cr = 1, ci = 0;
                    for (int k = 0; k < len / 2; k++)
                    {
                        int a = i + k, b = i + k + len / 2;
                        double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                        re[b] = re[a] - xr; im[b] = im[a] - xi; re[a] += xr; im[a] += xi;
                        double nr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = nr;
                    }
                }
            }
        }
    }

    // ---- Windows audio sessions (per-application volume, same thing as Windows' Volume mixer)
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        void GetAudioSessionControl(IntPtr g, int flags, out IAudioSessionControl c);
        void GetSimpleAudioVolume(IntPtr g, int flags, out ISimpleAudioVolume v);
        void GetSessionEnumerator(out IAudioSessionEnumerator e);
        void RegisterSessionNotification(IntPtr p); void UnregisterSessionNotification(IntPtr p);
        void RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr p); void UnregisterDuckNotification(IntPtr p);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator { void GetCount(out int n); void GetSession(int i, out IAudioSessionControl c); }
    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl
    {
        void GetState(out int s); void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string n); void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string n, ref Guid c);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string p); void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string p, ref Guid c);
        void GetGroupingParam(out Guid g); void SetGroupingParam(ref Guid g, ref Guid c);
        void RegisterAudioSessionNotification(IntPtr p); void UnregisterAudioSessionNotification(IntPtr p);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        void GetState(out int s); void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string n); void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string n, ref Guid c);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string p); void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string p, ref Guid c);
        void GetGroupingParam(out Guid g); void SetGroupingParam(ref Guid g, ref Guid c);
        void RegisterAudioSessionNotification(IntPtr p); void UnregisterAudioSessionNotification(IntPtr p);
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id); void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint pid); [PreserveSig] int IsSystemSoundsSession(); void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool b);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        void SetMasterVolume(float v, ref Guid ctx); void GetMasterVolume(out float v);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx); void GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation { void GetPeakValue(out float p); }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr p); void UnregisterControlChangeNotify(IntPtr p); void GetChannelCount(out uint n);
        void SetMasterVolumeLevel(float db, ref Guid ctx); void SetMasterVolumeLevelScalar(float v, ref Guid ctx);
        void GetMasterVolumeLevel(out float db); void GetMasterVolumeLevelScalar(out float v);
        void SetChannelVolumeLevel(uint ch, float db, ref Guid ctx); void SetChannelVolumeLevelScalar(uint ch, float v, ref Guid ctx);
        void GetChannelVolumeLevel(uint ch, out float db); void GetChannelVolumeLevelScalar(uint ch, out float v);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool m, ref Guid ctx); void GetMute([MarshalAs(UnmanagedType.Bool)] out bool m);
    }

    // One program as shown in the app-volume panel (all of its audio sessions on all playback devices, merged).
    class AppVol { public string Name; public float Volume; public bool Mute; public float Peak; }

    // Removes short noises (finger snaps and taps, keyboard and mouse clicks, bumps on the desk) but lets speech through.
    // The signal is delayed by 40 ms so every 5 ms frame can "look ahead" at what comes next.
    // A sudden jump in loudness that is NOT followed (within 40 ms) by a real voice sound is a click / snap / knock: it is muted
    // together with its whole ringing tail, even while you are talking. A "voice sound" = a clear pitch (70-400 Hz) in a sound that
    // has the normal brightness of a vowel: ringing plastic keys and snaps are too bright, a desk knock is too dull, noise has no pitch.
    class ClickGuard
    {
        const int F = 240, L = 8, W = 240;               // 5 ms frames; 8 frames (40 ms) look-ahead; pitch window = 240 samples at 12 kHz = 20 ms
        List<float> inq = new List<float>(); Queue<float> outq = new Queue<float>();
        Queue<float[]> fr = new Queue<float[]>();
        List<bool> act = new List<bool>(), voi = new List<bool>(), imp = new List<bool>(), impHf = new List<bool>();
        List<double> hfl = new List<double>();            // high-frequency loudness (dB) of the delayed frames
        List<float> ds = new List<float>();               // mono signal at 12 kHz for pitch detection
        double nf, prevE, prevHf; bool open, started, suppress; int hold; float gPrev = 1;
        public ClickGuard() { for (int i = 0; i < F * L * 2 + 64; i++) outq.Enqueue(0f); }

        // 0 = not a voice ... 1 = clear voice. A voice has a pitch (70-400 Hz) and does NOT repeat much faster than that; a ringing key or snap
        // is one fast tone (it repeats at lags far shorter than the pitch lag), so it is rejected.
        double Periodicity()
        {
            int n = ds.Count; if (n < W + 180) return 0; int s = n - W; double rPitch = 0, rSmall = 0;
            double e0 = 0; for (int i = 0; i < W; i++) e0 += ds[s + i] * ds[s + i]; if (e0 < 1e-9) return 0;
            for (int lag = 4; lag <= 171; lag++)
            {
                if (lag > 28 && lag < 30) continue;
                double c = 0, e1 = 0; for (int i = 0; i < W; i++) { double a = ds[s + i], b = ds[s + i - lag]; c += a * b; e1 += b * b; }
                double r = c / Math.Sqrt(e0 * e1 + 1e-12);
                if (lag < 30) { if (r > rSmall) rSmall = r; } else if (r > rPitch) rPitch = r;
            }
            return (rSmall < 0.9 * rPitch) ? rPitch : 0;
        }
        public void Process(float[] b, int n, float k)
        {
            double margin = 15.0 - 0.9 * k, jump = 18.0 - 0.8 * k, jumpHf = 12.0 - 0.5 * k; int need = 4 + (int)Math.Round(k * 0.4);
            for (int i = 0; i < n; i++) { inq.Add(b[2 * i]); inq.Add(b[2 * i + 1]); }
            while (inq.Count >= F * 2)
            {
                float[] f = new float[F * 2]; inq.CopyTo(0, f, 0, F * 2); inq.RemoveRange(0, F * 2);
                double s = 0; for (int i = 0; i < f.Length; i++) s += f[i] * f[i];
                double e = 10 * Math.Log10(s / f.Length + 1e-12);
                for (int i = 0; i + 3 < F; i += 4) ds.Add((f[2 * i] + f[2 * i + 1] + f[2 * i + 2] + f[2 * i + 3] + f[2 * i + 4] + f[2 * i + 5] + f[2 * i + 6] + f[2 * i + 7]) * 0.125f);
                if (ds.Count > 2 * W + 400) ds.RemoveRange(0, ds.Count - (W + 400));
                if (!started) { nf = e; prevE = e; started = true; }
                else if (e < nf) nf = nf * 0.8 + e * 0.2; else nf += 0.0025;           // noise floor: falls fast, rises very slowly
                if (nf > e) nf = e;
                bool a = e > nf + margin;
                // brightness = how much of the frame is high-frequency (0.0012 .. 0.2 is the range of vowels)
                double dsum = 0, ssum = 0; for (int i = 1; i < F; i++) { double dd = f[2 * i] - f[2 * (i - 1)]; dsum += dd * dd; ssum += f[2 * i] * (double)f[2 * i]; }
                double br = ssum > 0 ? dsum / ssum : 0;
                bool v = a && br > 0.0012 && br < 0.2 && Periodicity() > 0.5;
                double hf = 10 * Math.Log10(dsum / F + 1e-12);
                bool im = a && (e - prevE) > jump;                                       // sudden jump in loudness (click in silence)
                bool imh = a && (hf - prevHf) > jumpHf;                                  // sudden jump in the high frequencies (click in the middle of speech)
                prevE = e; prevHf = hf;
                fr.Enqueue(f); act.Add(a); voi.Add(v); imp.Add(im); impHf.Add(imh); hfl.Add(hf);
                if (fr.Count < L) continue;
                float[] o = fr.Dequeue(); bool a0 = act[0], v0 = voi[0], i0 = imp[0];
                int cnt = 0; bool anyV = false; for (int i = 0; i < L && i < act.Count; i++) { if (act[i]) cnt++; if (voi[i]) anyV = true; }
                bool hfFalls = hfl.Count > 2 && hfl[2] < hfl[0] - 6;                  // the bright part dies away within 10 ms (a fricative "s" does not)
                bool clickInSpeech = impHf[0] && hfFalls && !v0;                       // bright, quickly dying, not a voice: a key hit while talking
                act.RemoveAt(0); voi.RemoveAt(0); imp.RemoveAt(0); impHf.RemoveAt(0); hfl.RemoveAt(0);
                if (suppress) { if (v0 || !a0) suppress = false; }                       // the ring is over, or a voice started
                if (!suppress && ((i0 && !anyV) || clickInSpeech)) { suppress = true; open = false; }         // click / snap / knock: muted with its tail, even in the middle of speech
                if (!suppress)
                {
                    if (open) { if (a0) hold = 28; else if (--hold <= 0) open = false; }
                    else if (a0 && cnt >= need) { open = true; hold = 28; }              // sustained -> speech
                }
                float g = (suppress || (a0 && !open)) ? 0f : 1f;
                for (int i = 0; i < F; i++)
                {
                    float gg = g < gPrev ? g : gPrev + (g - gPrev) * (i + 1) / F;   // mute at once (the click is in the first milliseconds), come back softly
                    outq.Enqueue(o[2 * i] * gg); outq.Enqueue(o[2 * i + 1] * gg);
                }
                gPrev = g;
            }
            for (int i = 0; i < n; i++) { b[2 * i] = outq.Count > 0 ? outq.Dequeue() : 0f; b[2 * i + 1] = outq.Count > 0 ? outq.Dequeue() : 0f; }
        }
    }
    class Biquad
    {
        double b0 = 1, b1, b2, a1, a2; double[] z1 = new double[2], z2 = new double[2];
        public void Set(double nb0, double nb1, double nb2, double na0, double na1, double na2)
        { b0 = nb0 / na0; b1 = nb1 / na0; b2 = nb2 / na0; a1 = na1 / na0; a2 = na2 / na0; }
        public float Run(float x, int c)
        {
            double y = b0 * x + z1[c]; z1[c] = b1 * x - a1 * y + z2[c]; z2[c] = b2 * x - a2 * y; return (float)y;
        }
        public void Shelf(bool high, double db, double f, double fs)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / fs, cs = Math.Cos(w), al = Math.Sin(w) / 2 * Math.Sqrt(2), s = 2 * Math.Sqrt(A) * al;
            if (!high) Set(A * ((A + 1) - (A - 1) * cs + s), 2 * A * ((A - 1) - (A + 1) * cs), A * ((A + 1) - (A - 1) * cs - s), (A + 1) + (A - 1) * cs + s, -2 * ((A - 1) + (A + 1) * cs), (A + 1) + (A - 1) * cs - s);
            else Set(A * ((A + 1) + (A - 1) * cs + s), -2 * A * ((A - 1) + (A + 1) * cs), A * ((A + 1) + (A - 1) * cs - s), (A + 1) - (A - 1) * cs + s, 2 * ((A - 1) - (A + 1) * cs), (A + 1) - (A - 1) * cs - s);
        }
        public void Peak(double db, double f, double q, double fs)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / fs, cs = Math.Cos(w), al = Math.Sin(w) / (2 * q);
            Set(1 + al * A, -2 * cs, 1 - al * A, 1 + al / A, -2 * cs, 1 - al / A);
        }
    }

    class Ring
    {
        const int Cap = 48000; float[] d = new float[Cap * 2]; long w; long[] r = new long[8]; bool[] primed = new bool[8];
        public void Write(float[] s, int frames)
        {
            lock (this)
            {
                for (int i = 0; i < frames; i++) { int p = (int)((w + i) % Cap) * 2; d[p] = s[2 * i]; d[p + 1] = s[2 * i + 1]; }
                w += frames;
            }
        }
        public bool Read(int c, float[] dst, int frames)
        {
            lock (this)
            {
                long avail = w - r[c];
                if (avail > 2400) { r[c] = w - frames * 2; avail = frames * 2; }
                if (!primed[c]) { if (avail >= frames * 2) primed[c] = true; else return false; }
                if (avail < frames) { primed[c] = false; return false; }
                for (int i = 0; i < frames; i++) { int p = (int)((r[c] + i) % Cap) * 2; dst[2 * i] = d[p]; dst[2 * i + 1] = d[p + 1]; }
                r[c] += frames; return true;
            }
        }
    }

    class Worker { public Thread T; public volatile bool Stop; }

    // ------------------------------------------------------------ the engine + parameter store (own implementation)
    static class Eng
    {
        [DllImport("winmm")] static extern int timeBeginPeriod(int ms);
        [DllImport("ole32")] static extern int PropVariantClear(ref PROPVARIANT pv);

        [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
        static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string path, [In] ref Guid riid, IntPtr activationParams, IActivateAudioInterfaceCompletionHandler handler, out IActivateAudioInterfaceAsyncOperation op);

        static readonly Guid CLSID_Enum = new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E");
        static Guid IID_Client = new Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
        static Guid IID_Capture = new Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317");
        static Guid IID_Render = new Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");

        static ConcurrentDictionary<string, float> F = new ConcurrentDictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        static ConcurrentDictionary<string, string> S = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static volatile bool dirty, changed;
        public static string Status = "";
        // Short messages caused by something the user just did (update check, virtual mic...). Shown briefly, unlike Status (engine errors), which is not shown.
        public static string Notice = ""; public static DateTime NoticeAt = DateTime.MinValue;
        public static void Notify(string s) { Notice = s; NoticeAt = DateTime.Now; }

        static Ring[] rings = new Ring[8];
        static float[] stripL = new float[8], stripR = new float[8], busL = new float[8], busR = new float[8];
        static Worker[] stripW = new Worker[8], busW = new Worker[8];
        static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PotatoMixer", "settings.cfg"); } }

        // ---- public API used by the UI
        public static float Get(string n) { float v; F.TryGetValue(n, out v); return v; }
        public static void Set(string n, float v) { F[n] = v; changed = true; }
        public static void SetStr(string n, string v) { S[n] = v; changed = true; }
        // true when the microphones are quiet right now (used to pick a good moment to restart for an update)
        public static bool MicIdle() { return stripL[0] < 0.02f && stripR[0] < 0.02f && stripL[1] < 0.02f && stripR[1] < 0.02f; }
        public static string GetStr(string n) { string v; S.TryGetValue(n, out v); return v ?? ""; }
        public static bool Dirty() { bool d = dirty; dirty = false; return d; }
        public static float Level(int type, int ch)
        {
            float[] a; int i, side;
            if (type == 1) { if (ch < 10) { i = ch / 2; side = ch % 2; } else { i = 5 + (ch - 10) / 8; side = (ch - 10) % 8; } a = side == 0 ? stripL : stripR; if (side > 1) return 0; }
            else { i = ch / 8; side = ch % 8; if (side > 1) return 0; a = side == 0 ? busL : busR; }
            if (i < 0 || i > 7) return 0;
            float v = a[i]; a[i] = v * 0.82f; return v;
        }

        public static void Script(string script)
        {
            foreach (string raw in script.Split(';'))
            {
                string st = raw.Trim(); if (st.Length == 0) continue;
                int eq = st.IndexOf('='); if (eq < 0) continue;
                string key = st.Substring(0, eq).Trim(), val = st.Substring(eq + 1).Trim().Trim('"');
                Match m = Regex.Match(key, @"^(Strip|Bus)\[(\d)\]\.(.+)$", RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string kind = m.Groups[1].Value, prop = m.Groups[3].Value.ToLower(); int i = int.Parse(m.Groups[2].Value);
                    string pre = (kind.ToLower() == "strip" ? "Strip[" : "Bus[") + i + "]";
                    if (prop == "label") { S[pre + ".Label"] = val; changed = dirty = true; }
                    else if (prop == "app") { S[pre + ".App"] = val; changed = dirty = true; StartStrip(i); }
                    else if (prop.StartsWith("device.")) { S[pre + ".Device.name"] = val; changed = dirty = true; if (pre[0] == 'S') StartStrip(i); else StartBus(i); }
                    else { float f; if (float.TryParse(val, out f)) Set(pre + "." + m.Groups[3].Value, f); }
                    continue;
                }
                string cmd = key.ToLower();
                if (cmd == "command.restart") RestartAll();
                else if (cmd == "command.save") Save(val);
                else if (cmd == "command.load") { Load(val); RestartAll(); dirty = true; }
            }
        }

        // Name of the VB-Cable playback device ("CABLE Input ...") if the virtual cable is installed, else null.
        public static string FindCable()
        {
            try { foreach (string n in Devices(true)) if (n.StartsWith("CABLE Input", StringComparison.OrdinalIgnoreCase)) return n; } catch { }
            return null;
        }

        public static List<string> Devices(bool render)
        {
            List<string> r = new List<string>();
            foreach (KeyValuePair<string, string> d in Enum(render)) if (!r.Contains(d.Value)) r.Add(d.Value);
            return r;
        }

        // ---- lifecycle
        static Timer saver;
        public static void Start()
        {
            timeBeginPeriod(1);
            for (int i = 0; i < 8; i++) rings[i] = new Ring();
            // Read the saved settings FIRST. Saving is switched off until this succeeds, so a failed read can never overwrite your settings with empty ones.
            loaded = LoadSafe(SettingsPath);
            LogStart("settings loaded=" + loaded + " (" + F.Count + " numbers, " + S.Count + " texts)");
            // Autosave: anything changed is written to disk within about half a second, even if the program is killed or crashes later.
            int watchTick = 0;
            saver = new Timer(delegate
            {
                try { SaveIfChanged(false); } catch { }
                if (++watchTick % 20 == 0) { try { CableWatch(); } catch { } }   // every ~10 s
            }, null, 500, 500);
            AppDomain.CurrentDomain.ProcessExit += delegate { try { SaveIfChanged(true); } catch { } };
            try { AutoCable(); } catch (Exception ex) { LogError(ex); }
            RestartAll();
            StartAppWatch();
            LogStart("engine started");
        }
        public static void Shutdown() { for (int i = 0; i < 8; i++) { StopW(ref stripW[i]); StopW(ref busW[i]); } SaveIfChanged(true); }
        public static void SaveIfChanged(bool force) { if (changed || force) { changed = false; try { Save(SettingsPath); } catch { } } }
        // Like Voicemeeter's B1: if the virtual cable is installed, bus B1 is permanently its input, and Mic 1 / Mic 2 send to B1 by default.
        // Windows / Discord / games then just pick "CABLE Output" as their microphone.
        static void AutoCable()
        {
            string cable = FindCable(); if (cable == null) return;
            if (GetStr("Bus[5].Device.name") != cable) { S["Bus[5].Device.name"] = cable; changed = true; }
            if (GetStr("Bus[5].Label").Length == 0) { S["Bus[5].Label"] = "Mic out"; changed = true; }
            if (!F.ContainsKey("Strip[0].B1")) { F["Strip[0].B1"] = 1; F["Strip[1].B1"] = 1; changed = true; }
        }

        // If the virtual cable shows up later than the program (right after Windows starts), bind bus B1 to it then.
        static void CableWatch()
        {
            string cable = GetStr("Bus[5].Device.name");
            if (cable.Length > 0 && cable.StartsWith("CABLE Input", StringComparison.OrdinalIgnoreCase)) return;
            AutoCable();
            if (GetStr("Bus[5].Device.name").Length > 0) { dirty = true; StartBus(5); }
        }

        static void RestartAll() { for (int i = 0; i < 8; i++) { StartStrip(i); StartBus(i); } }

        static readonly object saveLock = new object();
        static volatile bool loaded;
        static void Save(string path)
        {
            if (!loaded && path == SettingsPath) return;   // never write over the settings file before it has been read successfully
            lock (saveLock) SaveCore(path);
        }

        // Reads the settings file (retrying if it is busy for a moment); falls back to the last good backup. Returns false only if a file exists but cannot be read.
        static bool LoadSafe(string path)
        {
            if (!File.Exists(path)) return true;   // first run
            for (int attempt = 0; attempt < 8; attempt++)
            {
                try { Load(path); try { File.Copy(path, path + ".bak", true); } catch { } return true; }
                catch (Exception ex) { LogError(ex); Thread.Sleep(250); }
            }
            try { if (File.Exists(path + ".bak")) { Load(path + ".bak"); return true; } } catch (Exception ex) { LogError(ex); }
            return false;
        }

        // Small log of each start (time, arguments, whether the settings were read) to help find problems that only happen at Windows start.
        public static void LogStart(string what)
        {
            try
            {
                string p = Path.Combine(Path.GetDirectoryName(SettingsPath), "startup.log");
                if (File.Exists(p) && new FileInfo(p).Length > 60000) File.Delete(p);
                File.AppendAllText(p, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  pid " + System.Diagnostics.Process.GetCurrentProcess().Id + "  " + what + "  [" + Environment.CommandLine + "]\n");
            }
            catch { }
        }
        static void SaveCore(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, float> k in F) sb.Append("F|" + k.Key + "|" + k.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
            foreach (KeyValuePair<string, string> k in S) sb.Append("S|" + k.Key + "|" + k.Value + "\n");
            // write to a temp file first, then swap it in, so a crash in the middle never leaves a half-written settings file
            string tmp = path + ".tmp"; File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
            if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
        }
        static void Load(string path)
        {
            if (!File.Exists(path)) return;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8))
            {
                string[] p = line.Split(new char[] { '|' }, 3); if (p.Length < 3) continue;
                if (p[0] == "F") { float v; if (float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) F[p[1]] = v; }
                else if (p[0] == "S") S[p[1]] = p[2];
            }
        }

        public static void LogError(Exception ex)
        {
            try { File.AppendAllText(Path.Combine(Path.GetDirectoryName(SettingsPath), "error.log"), DateTime.Now + " " + (ex == null ? "?" : ex.ToString()) + "\n"); } catch { }
        }

        static string BusName(int j) { string l = GetStr("Bus[" + j + "].Label"); return l.Length > 0 ? l : "A" + (j + 1); }

        static string Friendly(Exception ex)
        {
            COMException c = ex as COMException;
            if (c != null && (uint)c.ErrorCode == 0x8889000A) return "device is busy (another app uses it in exclusive mode)";
            if (c != null && (uint)c.ErrorCode == 0x88890008) return "device format not supported";
            return ex.Message;
        }

        static void StopW(ref Worker w) { if (w != null) { w.Stop = true; if (w.T != null) w.T.Join(500); w = null; } }

        // Runs an audio worker and starts it again 5 s after it fails or its device disappears. This is what makes it work right after Windows
        // starts (audio devices are not ready yet) and after a headset is unplugged and plugged back in.
        static void RetryLoop(Worker w, Action body, string what)
        {
            while (!w.Stop)
            {
                try { body(); } catch (Exception ex) { Status = what + ": " + Friendly(ex); LogError(ex); }
                for (int i = 0; i < 50 && !w.Stop; i++) Thread.Sleep(100);
            }
        }

        static void StartStrip(int i)
        {
            StopW(ref stripW[i]);
            if (i == 2) { StartApp(); return; }
            string name = GetStr("Strip[" + i + "].Device.name"); if (name.Length == 0) return;
            Worker w = new Worker(); stripW[i] = w;
            w.T = new Thread(delegate() { RetryLoop(w, delegate { CaptureLoop(i, name, w); }, "Strip " + (i + 1)); }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
            w.T.Start();
        }
        // ---- per-application audio (process loopback capture, Windows 10 build 19041+), shown as strip 3
        static void StartApp()
        {
            string name = GetStr("Strip[2].App"); if (name.Length == 0) return;
            Worker w = new Worker(); stripW[2] = w;
            w.T = new Thread(delegate() { try { AppLoop(name, w); } catch (Exception ex) { Status = "App: " + Friendly(ex); LogError(ex); } }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
            w.T.Start();
        }

        // ---- whole-device volume (what the Windows volume slider of that playback device does). Used from the UI thread only.
        static Dictionary<string, IAudioEndpointVolume> epCache = new Dictionary<string, IAudioEndpointVolume>();
        static Guid IID_EpVol = new Guid("5CDF2C82-841E-4546-9722-0CF74078229A");
        static IAudioEndpointVolume Ep(string name)
        {
            IAudioEndpointVolume v; if (epCache.TryGetValue(name, out v)) return v;
            foreach (KeyValuePair<string, string> d in Enum(true))
                if (d.Value == name)
                {
                    IMMDevice md; NewEnum().GetDevice(d.Key, out md); object o; Guid iid = IID_EpVol; md.Activate(ref iid, 23, IntPtr.Zero, out o);
                    v = (IAudioEndpointVolume)o; epCache[name] = v; return v;
                }
            return null;
        }
        public static bool GetDeviceVolume(string name, out float vol, out bool mute)
        {
            vol = 0; mute = false;
            try { IAudioEndpointVolume v = Ep(name); if (v == null) return false; v.GetMasterVolumeLevelScalar(out vol); v.GetMute(out mute); return true; }
            catch { epCache.Remove(name); return false; }
        }
        public static void SetDeviceVolume(string name, float vol)
        {
            try { IAudioEndpointVolume v = Ep(name); if (v != null) v.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, vol)), ref noCtx); } catch { epCache.Remove(name); }
        }
        public static void SetDeviceMute(string name, bool mute)
        {
            try { IAudioEndpointVolume v = Ep(name); if (v != null) v.SetMute(mute, ref noCtx); } catch { epCache.Remove(name); }
        }

        static Guid noCtx = Guid.Empty;
        static Guid IID_Sessions = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        // Calls act(processName, volume, meter, sessionInstanceId) for every audio session on every active playback device.
        static void EachSession(Action<string, ISimpleAudioVolume, IAudioMeterInformation, string> act)
        {
            foreach (KeyValuePair<string, string> dev in Enum(true))
            {
                try
                {
                    IMMDevice md; NewEnum().GetDevice(dev.Key, out md);
                    object o; Guid iid = IID_Sessions; md.Activate(ref iid, 23, IntPtr.Zero, out o);
                    IAudioSessionManager2 mgr = (IAudioSessionManager2)o; IAudioSessionEnumerator en; mgr.GetSessionEnumerator(out en);
                    int n; en.GetCount(out n);
                    for (int i = 0; i < n; i++)
                    {
                        try
                        {
                            IAudioSessionControl c; en.GetSession(i, out c);
                            IAudioSessionControl2 c2 = (IAudioSessionControl2)c; uint pid; c2.GetProcessId(out pid);
                            string name = "System sounds";
                            if (pid != 0) { try { name = System.Diagnostics.Process.GetProcessById((int)pid).ProcessName; } catch { continue; } }
                            if (name == "DsMaN") continue;
                            string inst; c2.GetSessionInstanceIdentifier(out inst);
                            act(name, (ISimpleAudioVolume)c, (IAudioMeterInformation)c, dev.Key + "|" + inst);
                        }
                        catch { }
                    }
                }
                catch { }
            }
        }

        public static List<AppVol> AppVolumes()
        {
            Dictionary<string, AppVol> d = new Dictionary<string, AppVol>(StringComparer.OrdinalIgnoreCase);
            EachSession(delegate(string name, ISimpleAudioVolume v, IAudioMeterInformation m, string id)
            {
                float vol; bool mute; float pk; v.GetMasterVolume(out vol); v.GetMute(out mute); m.GetPeakValue(out pk);
                AppVol a; if (!d.TryGetValue(name, out a)) { a = new AppVol { Name = name, Volume = vol, Mute = mute }; d[name] = a; }
                if (pk > a.Peak) a.Peak = pk;
            });
            List<AppVol> r = new List<AppVol>(d.Values); r.Sort(delegate(AppVol x, AppVol y) { return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase); });
            return r;
        }

        // Sets volume (0..1) and/or mute on every session of the program; the choice is remembered and re-applied when the program opens again.
        public static void SetAppVolume(string name, float vol, bool mute)
        {
            F["AppVol." + name + ".Vol"] = vol; F["AppVol." + name + ".Mute"] = mute ? 1 : 0; changed = true;
            ApplyApp(name, vol, mute);
        }
        static void ApplyApp(string name, float vol, bool mute)
        {
            EachSession(delegate(string n, ISimpleAudioVolume v, IAudioMeterInformation m, string id)
            { if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) { v.SetMasterVolume(vol, ref noCtx); v.SetMute(mute, ref noCtx); } });
        }

        // Background watcher: when a saved program shows up with a new audio session (e.g. Chrome restarted), its saved volume is applied once.
        static Thread appWatch; static HashSet<string> appSeen = new HashSet<string>();
        static void StartAppWatch()
        {
            if (appWatch != null) return;
            appWatch = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        EachSession(delegate(string n, ISimpleAudioVolume v, IAudioMeterInformation m, string id)
                        {
                            string key = "AppVol." + n + ".Vol"; float vol;
                            if (!F.TryGetValue(key, out vol)) return;
                            if (!appSeen.Add(n + "#" + id)) return;
                            v.SetMasterVolume(vol, ref noCtx); v.SetMute(Get("AppVol." + n + ".Mute") > 0, ref noCtx);
                        });
                    }
                    catch { }
                    Thread.Sleep(2000);
                }
            }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
            appWatch.Start();
        }

        // Windowed programs the user can pick: process name -> window title.
        public static List<KeyValuePair<string, string>> Apps()
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>(); int me = System.Diagnostics.Process.GetCurrentProcess().Id;
            string[] skip = { "explorer", "ApplicationFrameHost", "TextInputHost", "SystemSettings", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "LockApp", "Fasleeen", "Dasman", "DsMaN" };
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.Id == me || p.MainWindowHandle == IntPtr.Zero || p.MainWindowTitle.Length == 0 || Array.IndexOf(skip, p.ProcessName) >= 0) continue;
                    bool dup = false; foreach (KeyValuePair<string, string> k in r) if (k.Key == p.ProcessName) dup = true;
                    if (!dup) r.Add(new KeyValuePair<string, string>(p.ProcessName, p.MainWindowTitle));
                }
                catch { }
            }
            r.Sort(delegate(KeyValuePair<string, string> a, KeyValuePair<string, string> b) { return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase); });
            return r;
        }

        static System.Diagnostics.Process FindApp(string name)
        {
            System.Diagnostics.Process best = null;
            foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName(name)) { try { if (best == null || (p.MainWindowHandle != IntPtr.Zero && best.MainWindowHandle == IntPtr.Zero)) best = p; } catch { } }
            return best;
        }

        static IAudioClient ActivateLoopback(int pid)
        {
            IntPtr blob = Marshal.AllocHGlobal(12); IntPtr pv = Marshal.AllocHGlobal(24);
            try
            {
                Marshal.WriteInt32(blob, 0, 1); Marshal.WriteInt32(blob, 4, pid); Marshal.WriteInt32(blob, 8, 0);   // process loopback, include target tree
                for (int i = 0; i < 24; i += 8) Marshal.WriteInt64(pv, i, 0);
                Marshal.WriteInt16(pv, 0, 65); Marshal.WriteInt32(pv, 8, 12); Marshal.WriteIntPtr(pv, 16, blob);    // VT_BLOB
                ActivateHandler h = new ActivateHandler(); IActivateAudioInterfaceAsyncOperation op; Guid iid = IID_Client;
                ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, pv, h, out op);
                if (!h.Done.WaitOne(5000)) throw new Exception("application audio activation timed out");
                if (h.Hr < 0 || h.Iface == null) throw new Exception("application audio not available (needs Windows 10 2004 or newer)");
                IAudioClient ac = (IAudioClient)h.Iface;
                IntPtr fmt = MakeFormat(); Guid sess = Guid.Empty;
                ac.Initialize(0, unchecked((int)0x80000000) | 0x08000000 | 0x00020000, 1000000, 0, fmt, ref sess);
                return ac;
            }
            finally { Marshal.FreeHGlobal(blob); Marshal.FreeHGlobal(pv); }
        }

        static void AppLoop(string name, Worker w)
        {
            StripState st = new StripState(); float[] buf = new float[48000];
            while (!w.Stop)
            {
                System.Diagnostics.Process target = FindApp(name);
                if (target == null) { Status = name + " is not running"; Thread.Sleep(1000); continue; }
                IAudioClient ac = null;
                try
                {
                    ac = ActivateLoopback(target.Id);
                    object o; ac.GetService(ref IID_Capture, out o); IAudioCaptureClient cc = (IAudioCaptureClient)o;
                    ac.Start(); Status = "";
                    while (!w.Stop && !target.HasExited)
                    {
                        int pk; cc.GetNextPacketSize(out pk);
                        while (pk > 0 && !w.Stop)
                        {
                            IntPtr data; int frames, fl; long a, b; cc.GetBuffer(out data, out frames, out fl, out a, out b);
                            int n = Math.Min(frames, buf.Length / 2);
                            if ((fl & 2) != 0) Array.Clear(buf, 0, n * 2); else Marshal.Copy(data, buf, 0, n * 2);
                            cc.ReleaseBuffer(frames);
                            Process(2, st, buf, n);
                            cc.GetNextPacketSize(out pk);
                        }
                        Thread.Sleep(6);
                    }
                }
                catch (Exception ex) { Status = "App: " + Friendly(ex); LogError(ex); Thread.Sleep(2000); }
                finally { if (ac != null) { try { ac.Stop(); } catch { } } }
            }
        }

        static void StartBus(int j)
        {
            StopW(ref busW[j]);
            Worker w = new Worker(); busW[j] = w; string name = GetStr("Bus[" + j + "].Device.name");
            w.T = new Thread(delegate() { RetryLoop(w, delegate { BusLoop(j, name, w); }, BusName(j)); }) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
            w.T.Start();
        }

        // ---- device helpers
        static IMMDeviceEnumerator NewEnum() { return (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_Enum)); }

        static List<KeyValuePair<string, string>> Enum(bool render)
        {
            List<KeyValuePair<string, string>> r = new List<KeyValuePair<string, string>>();
            IMMDeviceEnumerator en = NewEnum(); IMMDeviceCollection col; en.EnumAudioEndpoints(render ? 0 : 1, 1, out col);
            int n; col.GetCount(out n);
            PROPERTYKEY key = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            for (int i = 0; i < n; i++)
            {
                IMMDevice d; col.Item(i, out d); string id; d.GetId(out id);
                IPropertyStore ps; d.OpenPropertyStore(0, out ps); PROPVARIANT pv; ps.GetValue(ref key, out pv);
                string name = Marshal.PtrToStringUni(pv.ptr); PropVariantClear(ref pv);
                r.Add(new KeyValuePair<string, string>(id, name ?? id));
            }
            return r;
        }

        static IntPtr MakeFormat()
        {
            IntPtr p = Marshal.AllocHGlobal(18);
            Marshal.WriteInt16(p, 0, 3); Marshal.WriteInt16(p, 2, 2); Marshal.WriteInt32(p, 4, 48000);
            Marshal.WriteInt32(p, 8, 48000 * 8); Marshal.WriteInt16(p, 12, 8); Marshal.WriteInt16(p, 14, 32); Marshal.WriteInt16(p, 16, 0);
            return p;
        }

        static IAudioClient Open(string name, bool render, bool loopback, out string err)
        {
            err = null; string id = null;
            foreach (KeyValuePair<string, string> d in Enum(render)) if (d.Value == name) { id = d.Key; break; }
            if (id == null) { err = "device not found: " + name; return null; }
            IMMDevice dev; NewEnum().GetDevice(id, out dev);
            object o; dev.Activate(ref IID_Client, 23, IntPtr.Zero, out o);
            IAudioClient ac = (IAudioClient)o;
            IntPtr fmt = MakeFormat(); Guid sess = Guid.Empty;
            int flags = unchecked((int)0x80000000) | 0x08000000 | (loopback ? 0x00020000 : 0);
            ac.Initialize(0, flags, 1000000, 0, fmt, ref sess);
            return ac;
        }

        static float Db(float lin) { return lin > 1e-6f ? 20f * (float)Math.Log10(lin) : -120f; }

        // ---- strip: capture -> process -> ring
        class StripState
        {
            public Biquad[] eq = { new Biquad(), new Biquad(), new Biquad() };
            public float prevG = 1, gate = 1, comp = 1; public Denoiser dn; public ClickGuard cg; public int gateHold;
        }

        static void CaptureLoop(int i, string name, Worker w)
        {
            bool loop = i >= 5; string err;
            IAudioClient ac = Open(name, loop, loop, out err);
            if (ac == null) { Status = "Strip " + (i + 1) + ": " + err; return; }
            object o; ac.GetService(ref IID_Capture, out o); IAudioCaptureClient cc = (IAudioCaptureClient)o;
            ac.Start();
            StripState st = new StripState(); float[] buf = new float[48000];
            Status = "";
            try
            {
                while (!w.Stop)
                {
                    int pk; cc.GetNextPacketSize(out pk);
                    while (pk > 0 && !w.Stop)
                    {
                        IntPtr data; int frames, fl; long a, b; cc.GetBuffer(out data, out frames, out fl, out a, out b);
                        int n = Math.Min(frames, buf.Length / 2);
                        if ((fl & 2) != 0) Array.Clear(buf, 0, n * 2); else Marshal.Copy(data, buf, 0, n * 2);
                        cc.ReleaseBuffer(frames);
                        Process(i, st, buf, n);
                        cc.GetNextPacketSize(out pk);
                    }
                    Thread.Sleep(6);
                }
            }
            finally { try { ac.Stop(); } catch { } }
        }

        // Parameter keys are built once so the audio threads do not create strings every block.
        // Strip keys: 0 Gain,1 Mute,2 Solo,3 Mono,4 MC,5 Pan_x,6-8 EQGain1-3,9 Denoiser,10 Gate,11 Comp,12-19 routes A1..A5,B1..B3 (= bus 0..7)
        static readonly string[,] SK = MakeSK();
        static readonly string[,] BK = MakeBK();
        static string[,] MakeSK()
        {
            string[] props = { "Gain", "Mute", "Solo", "Mono", "MC", "Pan_x", "EQGain1", "EQGain2", "EQGain3", "Denoiser", "Gate", "Comp", "A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3", "Clicks" };
            string[,] r = new string[8, props.Length];
            for (int s = 0; s < 8; s++) for (int k = 0; k < props.Length; k++) r[s, k] = "Strip[" + s + "]." + props[k];
            return r;
        }
        static string[,] MakeBK()
        {
            string[,] r = new string[8, 3];
            for (int j = 0; j < 8; j++) { r[j, 0] = "Bus[" + j + "].Gain"; r[j, 1] = "Bus[" + j + "].Mute"; r[j, 2] = "Bus[" + j + "].Mono"; }
            return r;
        }

        static void Process(int i, StripState st, float[] b, int n)
        {
            bool anySolo = false; for (int k = 0; k < 8; k++) if (Get(SK[k, 2]) > 0) anySolo = true;
            bool audible = !(Get(SK[i, 1]) > 0) && (!anySolo || Get(SK[i, 2]) > 0);
            bool mono = Get(SK[i, 3]) > 0 || Get(SK[i, 4]) > 0;
            float lin = (float)Math.Pow(10, Get(SK[i, 0]) / 20.0);
            float px = Get(SK[i, 5]); float pl = 1f - Math.Max(0f, px * 2f), pr = 1f - Math.Max(0f, -px * 2f);
            // EQ
            float g1 = Get(SK[i, 6]), g2 = Get(SK[i, 7]), g3 = Get(SK[i, 8]);
            bool eqOn = Math.Abs(g1) > 0.05f || Math.Abs(g2) > 0.05f || Math.Abs(g3) > 0.05f;
            if (eqOn) { st.eq[0].Shelf(false, g1, 150, 48000); st.eq[1].Peak(g2, 1000, 0.8, 48000); st.eq[2].Shelf(true, g3, 6000, 48000); }
            // noise suppression first, so the gate and compressor see the cleaned signal
            float dk = Get(SK[i, 9]);
            if (dk > 0.05f) { if (st.dn == null) st.dn = new Denoiser(); st.dn.Process(b, n, dk); } else st.dn = null;
            // short noises (finger taps, key and mouse clicks) are muted before the gate looks at the level
            float xk = Get(SK[i, 20]);
            if (xk > 0.05f) { if (st.cg == null) st.cg = new ClickGuard(); st.cg.Process(b, n, xk); } else st.cg = null;
            // block level for gate / compressor
            double sum = 0; for (int k = 0; k < n * 2; k++) sum += b[k] * b[k];
            float rmsDb = Db((float)Math.Sqrt(sum / (n * 2)));
            float gk = Get(SK[i, 10]), ck = Get(SK[i, 11]);
            if (gk > 0)
            {
                // threshold: strength 1..10 -> -62 .. -26 dB. The gate stays open for 150 ms after the voice drops, so words are not chopped.
                bool above = rmsDb > -66f + gk * 4f; if (above) st.gateHold = 7200; else st.gateHold -= n;
                float tgt = st.gateHold > 0 ? 1f : 0f; st.gate += (tgt - st.gate) * (tgt > st.gate ? 0.7f : 0.15f);
            }
            else st.gate = 1f;
            if (ck > 0)
            {
                float thr = -3f * ck, red = rmsDb > thr ? (rmsDb - thr) * 0.75f : 0f;
                float tgt = (float)Math.Pow(10, (-red + ck * 0.8f) / 20.0); st.comp += (tgt - st.comp) * 0.25f;
            }
            else st.comp = 1f;
            float cur = audible ? lin * st.gate * st.comp : 0f, prev = st.prevG; st.prevG = cur;
            float lv = 0, rv = 0;
            for (int k = 0; k < n; k++)
            {
                float l = b[2 * k], r = b[2 * k + 1];
                if (eqOn) { for (int e = 0; e < 3; e++) { l = st.eq[e].Run(l, 0); r = st.eq[e].Run(r, 1); } }
                if (mono) { l = r = (l + r) * 0.5f; }
                float g = prev + (cur - prev) * (k + 1) / n;
                l *= g * pl; r *= g * pr; b[2 * k] = l; b[2 * k + 1] = r;
                float al = Math.Abs(l), ar = Math.Abs(r); if (al > lv) lv = al; if (ar > rv) rv = ar;
            }
            if (lv > stripL[i]) stripL[i] = lv; if (rv > stripR[i]) stripR[i] = rv;
            rings[i].Write(b, n);
        }

        // ---- bus: rings -> mix -> device
        static void MixBus(int j, float[] outb, float[] tmp, int n)
        {
            Array.Clear(outb, 0, n * 2);
            for (int s = 0; s < 8; s++)
            {
                bool routed = s == 2 ? (Get(SK[0, 12 + j]) > 0 || Get(SK[1, 12 + j]) > 0) : Get(SK[s, 12 + j]) > 0;
                if (!routed) continue;
                if (s >= 5) { string sd = GetStr("Strip[" + s + "].Device.name"); if (sd.Length > 0 && sd == GetStr("Bus[" + j + "].Device.name")) { Status = "Feedback blocked: " + sd; continue; } }
                if (!rings[s].Read(j, tmp, n)) continue;
                for (int k = 0; k < n * 2; k++) outb[k] += tmp[k];
            }
            float g = Get(BK[j, 1]) > 0 ? 0f : (float)Math.Pow(10, Get(BK[j, 0]) / 20.0);
            bool mono = Get(BK[j, 2]) > 0; float lv = 0, rv = 0;
            for (int k = 0; k < n; k++)
            {
                float l = outb[2 * k], r = outb[2 * k + 1];
                if (mono) l = r = (l + r) * 0.5f;
                l = Math.Max(-1f, Math.Min(1f, l * g)); r = Math.Max(-1f, Math.Min(1f, r * g));
                outb[2 * k] = l; outb[2 * k + 1] = r;
                float al = Math.Abs(l), ar = Math.Abs(r); if (al > lv) lv = al; if (ar > rv) rv = ar;
            }
            if (lv > busL[j]) busL[j] = lv; if (rv > busR[j]) busR[j] = rv;
        }

        static void BusLoop(int j, string name, Worker w)
        {
            const int N = 480; float[] outb = new float[N * 2], tmp = new float[N * 2];
            IAudioClient ac = null; IAudioRenderClient rc = null; uint size = 0;
            if (name.Length > 0)
            {
                string err; ac = Open(name, true, false, out err);
                if (ac == null) { Status = BusName(j) + ": " + err; }
                else { object o; ac.GetService(ref IID_Render, out o); rc = (IAudioRenderClient)o; ac.GetBufferSize(out size); ac.Start(); }
            }
            try
            {
                if (rc == null)
                {
                    if (name.Length > 0) return;   // the device could not be opened: let the retry loop try again later
                    while (!w.Stop) Thread.Sleep(100);   // no device chosen: nothing to mix or play
                    return;
                }
                while (!w.Stop)
                {
                    uint pad; ac.GetCurrentPadding(out pad);
                    while (pad < 1920 && pad + N <= size && !w.Stop)
                    {
                        MixBus(j, outb, tmp, N);
                        IntPtr dst; rc.GetBuffer(N, out dst); Marshal.Copy(outb, 0, dst, N * 2); rc.ReleaseBuffer(N, 0);
                        pad += N;
                    }
                    Thread.Sleep(5);
                }
            }
            finally { if (ac != null) { try { ac.Stop(); } catch { } } }
        }
    }
}
