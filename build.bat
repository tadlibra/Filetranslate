@echo off
title Beeslater - Build Tool (.NET)
color 0B
echo.
echo  ================================================
echo   Beeslater - Build EXE (.NET 8)
echo  ================================================
echo.

dotnet --version >nul 2>&1
if errorlevel 1 (
    echo  [ERROR] .NET SDK not found.
    echo  Download: https://dotnet.microsoft.com/download
    pause
    exit /b 1
)
echo  [OK] .NET SDK ready
echo.

echo  [1/4] Restoring packages...
dotnet restore
if errorlevel 1 (
    echo  [ERROR] Restore failed.
    pause
    exit /b 1
)
echo  [OK] Restore completed
echo.

echo  [2/4] Publishing single-file EXE...
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
if errorlevel 1 (
    echo  [ERROR] Publish failed.
    pause
    exit /b 1
)
echo  [OK] Publish completed
echo.

echo  [3/4] Copying EXE to project root...
copy /Y "bin\Release\net8.0-windows\win-x64\publish\Beeslater.exe" "Beeslater.exe" >nul
if errorlevel 1 (
    echo  [ERROR] Could not copy Beeslater.exe to project root.
    pause
    exit /b 1
)
echo  [OK] Root EXE ready: Beeslater.exe
echo.

echo  [4/4] Done!
echo.
echo  Output:
echo  .\Beeslater.exe
echo.

pause
