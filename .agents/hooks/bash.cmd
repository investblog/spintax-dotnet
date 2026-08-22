@echo off
rem Runs a POSIX hook script with Git for Windows' bash, found next to git.exe on PATH - never the
rem bare `bash`, which on Windows may be the WSL launcher. Usage: bash.cmd <script.sh> [args]
setlocal
for /f "delims=" %%G in ('where git 2^>nul') do (set "GITEXE=%%G" & goto found)
echo bash.cmd: git.exe not found on PATH - install Git for Windows >&2
exit /b 1
:found
for %%D in ("%GITEXE%\..\..") do set "GITROOT=%%~fD"
if not exist "%GITROOT%\bin\bash.exe" (
  echo bash.cmd: %GITROOT%\bin\bash.exe not found >&2
  exit /b 1
)
"%GITROOT%\bin\bash.exe" %*
