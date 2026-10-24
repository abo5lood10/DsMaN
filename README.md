# DsMaN

**Audio mixer for Windows** – one program to control your headphones, clean your microphone (denoiser, gate, compressor) and send it to Discord / games. No Voicemeeter needed.

**خلاط صوت لويندوز** – برنامج واحد تتحكم فيه بصوت السماعات، وتنظّف المايك (دينويزر، جيت، كومب)، وترسله لديسكورد والألعاب.

## Download / التحميل
Go to **Releases** and download `DsMaN.exe`. Run it – no install needed.
من صفحة **Releases** نزّل `DsMaN.exe` وشغّله مباشرة.

## Features
- Microphone strips with **Denoiser / Gate / Compressor** (right-click and drag a knob)
- Real volume + mute for each headphone (the faders follow the Windows device volume)
- **B1 = virtual microphone** for Discord and games (with VB-Cable: choose `CABLE Output` as the mic)
- Per-app volume (App volume) and sharing an app's sound with your mic (Share app)
- Auto-save of every change, **automatic updates**, start with Windows
- Built-in Arabic guide: *Settings → دليل استخدام*

## Requirements
- Windows 10 (2004+) / Windows 11, 64-bit
- For the virtual microphone: [VB-CABLE](https://vb-audio.com/Cable/) (free, from VB-Audio)

## Updates
The program checks `update.txt` in this repository (`version=` and `url=`) and updates itself.
To publish a new version: build, create a GitHub **Release** with `DsMaN.exe`, then raise `version=` in `update.txt`.

## Build
Run `build.bat` (uses the .NET Framework C# compiler that ships with Windows).
The UI font (thmanyah Sans) is **not** included in this repository because its licence does not allow redistributing the font files; without it the program falls back to a system font. Release builds contain the font embedded.

## Notes
- VB-CABLE is a separate product by VB-Audio and is not part of this repository.
