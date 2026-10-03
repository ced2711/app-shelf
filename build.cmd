@echo off
rem Builds AppShelf.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
rem   build.cmd        -> AppShelf.exe
rem   build.cmd test   -> AppShelfTest.exe (console self-test)
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=/r:System.Windows.Forms.dll /r:System.Drawing.dll
cd /d "%~dp0"

if /i "%1"=="test" (
  "%CSC%" /nologo /target:exe /optimize+ /define:SELFTEST %REFS% /out:AppShelfTest.exe src\AppShelf.cs tests\SelfTest.cs
) else (
  "%CSC%" /nologo /target:winexe /optimize+ %REFS% /out:AppShelf.exe src\AppShelf.cs
)
