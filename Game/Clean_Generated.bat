@echo off
setlocal EnableExtensions
rem Place this file in the Game root folder (the one that contains GameData and GameCommon).
rem   GameData            : keep only *.xlsx, delete every other file and subfolder
rem   GameCommon\Generated: keep only Game.Generated.csproj, delete everything else
rem The window stays open (pause) and every failure is printed and written to Clean_Generated.log.

set "ROOT=%~dp0"
set "DATA_DIR=%ROOT%GameData"
set "GEN_DIR=%ROOT%GameCommon\Generated"
set "LOG=%ROOT%Clean_Generated.log"
set "FAILED=0"
set "SHUT="
echo Clean_Generated %DATE% %TIME% > "%LOG%"
echo ROOT=%ROOT%>> "%LOG%"

call :Main
echo.
echo Log: %LOG%
pause
endlocal
exit /b 0

:Main
if not exist "%DATA_DIR%\" (
    echo [ERROR] Folder not found: %DATA_DIR%
    exit /b 1
)
if not exist "%GEN_DIR%\" (
    echo [ERROR] Folder not found: %GEN_DIR%
    exit /b 1
)

echo ROOT   : %ROOT%
echo Target 1: %DATA_DIR%   (keep *.xlsx only)
echo Target 2: %GEN_DIR%   (keep Game.Generated.csproj only)
echo.
echo Programs that lock obj\ files (close them first if listed):
for %%P in (devenv.exe Code.exe rider64.exe dotnet.exe MSBuild.exe VBCSCompiler.exe ServiceHub.Host.dotnet.x64.exe) do (
    tasklist /fi "imagename eq %%P" 2>nul | find /i "%%P" >nul && echo    running: %%P
)
echo.
choice /c YN /m "Delete the other files now"
if errorlevel 2 (
    echo Canceled.
    exit /b 0
)

for /f "delims=" %%F in ('dir /b /a-d "%DATA_DIR%" 2^>nul') do (
    if /i not "%%~xF"==".xlsx" call :RmFile "%DATA_DIR%\%%F" "GameData\%%F"
)
for /f "delims=" %%D in ('dir /b /ad "%DATA_DIR%" 2^>nul') do call :RmDir "%DATA_DIR%\%%D" "GameData\%%D"

for /f "delims=" %%F in ('dir /b /a-d "%GEN_DIR%" 2^>nul') do (
    if /i not "%%F"=="Game.Generated.csproj" call :RmFile "%GEN_DIR%\%%F" "GameCommon\Generated\%%F"
)
for /f "delims=" %%D in ('dir /b /ad "%GEN_DIR%" 2^>nul') do call :RmDir "%GEN_DIR%\%%D" "GameCommon\Generated\%%D"

echo.
echo Re-checking in 4 seconds (an IDE may re-create obj right after it is deleted)...
timeout /t 4 /nobreak >nul
for /f "delims=" %%D in ('dir /b /ad "%GEN_DIR%" 2^\>nul') do (
    echo [RECREATED] GameCommon\Generated\%%D  - a running Visual Studio / dotnet restore made it again. Close them and run this again.
    echo RECREATED %%D >> "%LOG%"
    set /a FAILED+=1
)
if "%FAILED%"=="0" (echo Done.) else (echo Done, but %FAILED% item^(s^) could not be deleted. See above / the log.)
exit /b 0

:RmFile
attrib -r -s -h "%~1" >nul 2>&1
del /f /q "%~1" >nul 2>&1
if exist "%~1" (
    echo [FAILED ] %~2
    echo FAILED file %~2 >> "%LOG%"
    set /a FAILED+=1
) else (
    echo deleted: %~2
    echo deleted file %~2 >> "%LOG%"
)
exit /b 0

:RmDir
rem 1) clear attributes, normal remove
attrib -r -s -h "%~1" /s /d >nul 2>&1
rd /s /q "%~1" >nul 2>&1
if not exist "%~1" goto :RmDirOk
rem 2) stop build servers once, retry a few times (antivirus/indexer can hold files for a moment)
if not defined SHUT (
    set "SHUT=1"
    dotnet build-server shutdown >nul 2>&1
    taskkill /f /im VBCSCompiler.exe >nul 2>&1
    taskkill /f /im MSBuild.exe >nul 2>&1
)
for %%N in (1 2 3) do (
    if exist "%~1" (
        timeout /t 1 /nobreak >nul
        rd /s /q "%~1" >nul 2>&1
    )
)
if not exist "%~1" goto :RmDirOk
rem 3) empty-folder mirror (long paths / odd names), then remove the shell
set "EMPTY=%TEMP%\clean_empty_%RANDOM%"
mkdir "%EMPTY%" >nul 2>&1
robocopy "%EMPTY%" "%~1" /mir /njh /njs /np /nfl /ndl /r:1 /w:1 >nul 2>&1
rd /q "%EMPTY%" >nul 2>&1
rd /s /q "%~1" >nul 2>&1
if not exist "%~1" goto :RmDirOk
echo [FAILED ] folder %~2
echo FAILED folder %~2 >> "%LOG%"
rd /s /q "%~1" >> "%LOG%" 2>&1
rd /s /q "%~1" 2>&1 | more
dir /s /b /a "%~1" 2>nul | find /c /v "" 
set /a FAILED+=1
exit /b 0
:RmDirOk
echo deleted folder: %~2
echo deleted folder %~2 >> "%LOG%"
exit /b 0
