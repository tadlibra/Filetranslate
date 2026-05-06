@echo off
chcp 65001 >nul
title MC Mod Translator - Build Tool
color 0A

:: Chuyen vao thu muc chua file .bat nay
cd /d "%~dp0"

echo.
echo  ==========================================
echo     MC Mod Translator - Build .EXE
echo  ==========================================
echo.

:: Kiem tra Python
python --version >nul 2>&1
if errorlevel 1 (
    echo  [LOI] Khong tim thay Python. Cai Python 3.10+ tai python.org
    pause
    exit /b 1
)
echo  [OK] Python da san sang

:: Kiem tra pip
pip --version >nul 2>&1
if errorlevel 1 (
    echo  [LOI] Khong tim thay pip.
    pause
    exit /b 1
)

echo.
echo  [1/4] Cai thu vien...
python -m pip install --quiet --upgrade customtkinter deep-translator anthropic pyinstaller pillow
if errorlevel 1 (
    echo  [LOI] Cai thu vien that bai.
    pause
    exit /b 1
)
echo  [OK] Thu vien da san sang

echo.
echo  [2/4] Don dep build cu...
if exist "dist\MC_Mod_Translator.exe" del /f /q "dist\MC_Mod_Translator.exe"
if exist "build" rmdir /s /q "build"
echo  [OK] Da don dep

echo.
echo  [3/4] Dang build .exe (co the mat 1-3 phut)...
python -m PyInstaller --clean MC_Mod_Translator.spec
if errorlevel 1 (
    echo.
    echo  [LOI] Build that bai. Xem log phia tren.
    pause
    exit /b 1
)

echo.
echo  [4/4] Kiem tra ket qua...
if exist "dist\MC_Mod_Translator.exe" (
    echo.
    echo  ==========================================
    echo           BUILD THANH CONG!
    echo  ==========================================
    echo.
    echo  File .exe: dist\MC_Mod_Translator.exe
    for %%A in ("dist\MC_Mod_Translator.exe") do echo  Kich thuoc: %%~zA bytes
    echo.
    echo  Nhan phim bat ky de mo thu muc dist...
    pause >nul
    explorer dist
) else (
    echo  [LOI] Khong tim thay file .exe sau khi build.
    pause
    exit /b 1
)
