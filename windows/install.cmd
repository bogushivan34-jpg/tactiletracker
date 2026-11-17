@echo off
rem Builds (if needed) and installs Tactile Issues for the current user, with a Start menu shortcut.
rem No admin rights needed. Uninstall: delete %LOCALAPPDATA%\Programs\TactileIssues and the shortcut.
setlocal
cd /d "%~dp0"
if not exist bin\Tactile.exe call build.cmd || exit /b 1
set DEST=%LOCALAPPDATA%\Programs\TactileIssues
if not exist "%DEST%" mkdir "%DEST%"
xcopy /y /q /e /i bin "%DEST%" >nul
powershell -NoProfile -Command "$s=(New-Object -ComObject WScript.Shell).CreateShortcut([Environment]::GetFolderPath('Programs')+'\Tactile Issues.lnk'); $s.TargetPath='%DEST%\Tactile.exe'; $s.WorkingDirectory='%DEST%'; $s.Save()"
echo Installed to %DEST%  (Start menu: "Tactile Issues")
