@echo off
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set RES=/resource:src\logo.png,logo.png /resource:src\head.png,head.png /resource:src\guide.txt,guide.txt /resource:src\name.bin,name.bin
if exist src\fonts\f1.bin set RES=%RES% /resource:src\fonts\f1.bin,f1.bin /resource:src\fonts\f2.bin,f2.bin
%CSC% /target:winexe /platform:x64 /optimize+ %RES% /win32icon:src\logo.ico /out:DsMaN.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\Engine.cs src\Ui.cs
