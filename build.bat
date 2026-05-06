@echo off
title Beeslater - Build Tool
color 0B
echo.
echo  ================================================
echo   Beeslater - Build EXE
echo  ================================================
echo.

python --version >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] Python not found!
    echo  Download: https://www.python.org/downloads/
    echo  Check "Add Python to PATH" when installing.
    pause
    exit /b 1
)
echo  [OK] Python ready
echo.

:: Kill old EXE if running
echo  [0/3] Closing old Beeslater.exe if running...
taskkill /f /im Beeslater.exe >nul 2>&1
timeout /t 1 /nobreak >nul
echo  [OK] Ready to build
echo.

echo  [1/3] Installing dependencies...
pip install customtkinter deep-translator pyinstaller --quiet --upgrade
if errorlevel 1 (
    echo  [ERROR] Install failed. Check internet connection.
    pause
    exit /b 1
)
echo  [OK] Dependencies installed
echo.

echo  [2/3] Building EXE... (1-2 minutes)
echo.

python -m PyInstaller ^
    --onefile ^
    --windowed ^
    --name "Beeslater" ^
    --icon "icon.ico" ^
    --add-data "icon.ico;." ^
    --hidden-import customtkinter ^
    --hidden-import deep_translator ^
    --hidden-import deep_translator.engines ^
    --hidden-import deep_translator.engines.google ^
    --collect-all customtkinter ^
    --clean ^
    --noconfirm ^
    mc_translator_app.py

if errorlevel 1 (
    echo.
    echo  [ERROR] Build failed! See log above.
    pause
    exit /b 1
)

echo.
echo  [3/3] Done!
echo.
echo  ================================================
echo   EXE saved at: dist\Beeslater.exe
echo  ================================================
echo.

explorer dist
pause
