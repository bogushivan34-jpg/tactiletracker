@echo off
rem Regenerates assets\app.ico and assets\icon-512.png from tools\IconGen.cs
setlocal
cd /d "%~dp0.."
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
"%FW%\csc.exe" /nologo /target:exe /out:tools\IconGen.exe ^
  /r:"%FW%\WPF\PresentationCore.dll" /r:"%FW%\WPF\PresentationFramework.dll" /r:"%FW%\WPF\WindowsBase.dll" /r:"%FW%\System.Xaml.dll" ^
  tools\IconGen.cs || exit /b 1
tools\IconGen.exe assets
del tools\IconGen.exe
