@echo off
rem Builds AppShelf.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem   build.cmd        -> AppShelf.exe
rem   build.cmd test   -> AppShelfTest.exe (console self-test)
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set OPTS=/nologo /optimize+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll
cd /d "%~dp0"
if not exist obj mkdir obj

rem Pass 1: render the exe icon from the same drawing code the app uses.
"%CSC%" %OPTS% /target:winexe /out:obj\icon-gen.exe src\*.cs || exit /b 1
start "" /wait obj\icon-gen.exe --write-icon obj\AppShelf.ico
del obj\icon-gen.exe

rem Pass 2: the real exe, with the icon.
if /i "%1"=="test" (
  "%CSC%" %OPTS% /target:exe /define:SELFTEST /win32icon:obj\AppShelf.ico /out:AppShelfTest.exe src\*.cs tests\SelfTest.cs
) else (
  "%CSC%" %OPTS% /target:winexe /win32icon:obj\AppShelf.ico /out:AppShelf.exe src\*.cs
)
