@echo off
title Beeslater - Dev (hot reload)
color 0A
echo.
echo  ================================================
echo   Beeslater - DEV MODE (auto rebuild + restart)
echo  ================================================
echo.
echo  Sua code roi Ctrl+S -^> app tu build lai va mo lai.
echo  Nhan Ctrl+C 2 lan de thoat.
echo.

dotnet watch run -c Debug
