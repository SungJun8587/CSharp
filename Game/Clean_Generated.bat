@echo off
setlocal EnableExtensions
rem Place this file in the Game root folder (the one that contains GameData and GameCommon).
rem   GameData            : keep only *.xlsx, delete every other file and subfolder
rem   GameCommon\Generated: keep only Game.Generated.csproj, delete everything else

set "ROOT=%~dp0"
set "DATA_DIR=%ROOT%GameData"
set "GEN_DIR=%ROOT%GameCommon\Generated"

if not exist "%DATA_DIR%\" (
    echo [ERROR] Folder not found: %DATA_DIR%
    exit /b 1
)
if not exist "%GEN_DIR%\" (
    echo [ERROR] Folder not found: %GEN_DIR%
    exit /b 1
)

echo Target 1: %DATA_DIR%   (keep *.xlsx only)
echo Target 2: %GEN_DIR%   (keep Game.Generated.csproj only)
choice /c YN /m "Delete the other files now"
if errorlevel 2 (
    echo Canceled.
    exit /b 0
)

rem ---- GameData : files other than *.xlsx (hidden files included) ----
for /f "delims=" %%F in ('dir /b /a-d "%DATA_DIR%" 2^>nul') do (
    if /i not "%%~xF"==".xlsx" (
        del /f /q /a "%DATA_DIR%\%%F" >nul 2>&1
        echo deleted: GameData\%%F
    )
)
rem ---- GameData : subfolders ----
for /f "delims=" %%D in ('dir /b /ad "%DATA_DIR%" 2^>nul') do (
    rd /s /q "%DATA_DIR%\%%D" >nul 2>&1
    echo deleted folder: GameData\%%D
)

rem ---- GameCommon\Generated : files other than Game.Generated.csproj ----
for /f "delims=" %%F in ('dir /b /a-d "%GEN_DIR%" 2^>nul') do (
    if /i not "%%F"=="Game.Generated.csproj" (
        del /f /q /a "%GEN_DIR%\%%F" >nul 2>&1
        echo deleted: GameCommon\Generated\%%F
    )
)
rem ---- GameCommon\Generated : subfolders (obj, bin, ...) ----
for /f "delims=" %%D in ('dir /b /ad "%GEN_DIR%" 2^>nul') do (
    rd /s /q "%GEN_DIR%\%%D" >nul 2>&1
    echo deleted folder: GameCommon\Generated\%%D
)

echo Done.
endlocal
