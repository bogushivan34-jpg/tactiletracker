@echo off
rem Builds Tactile.exe with the C# compiler that ships inside Windows (.NET Framework 4.8).
rem No SDK, Node, or Visual Studio needed. Output: windows\bin\
setlocal
cd /d "%~dp0"
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FW%\csc.exe" set FW=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist bin mkdir bin

"%FW%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /utf8output /codepage:65001 ^
  /out:bin\Tactile.exe ^
  /win32icon:assets\app.ico ^
  /win32manifest:assets\app.manifest ^
  /r:"%FW%\WPF\PresentationCore.dll" ^
  /r:"%FW%\WPF\PresentationFramework.dll" ^
  /r:"%FW%\WPF\WindowsBase.dll" ^
  /r:"%FW%\System.Xaml.dll" ^
  /r:System.dll /r:System.Core.dll ^
  src\*.cs
if errorlevel 1 exit /b 1

copy /y assets\Tactile.exe.config bin\Tactile.exe.config >nul
if not exist bin\config.json if exist config.json copy /y config.json bin\config.json >nul
if exist fonts xcopy /y /q /i fonts bin\fonts >nul
echo Built bin\Tactile.exe
