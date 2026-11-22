@echo off
rem 一键编译。产物在 src\bin\Release\WuwaChecklist.exe
setlocal
cd /d "%~dp0..\src"

rem 程序开着的时候编译会报文件被占用，先关掉
taskkill /f /im WuwaChecklist.exe >nul 2>&1
timeout /t 1 /nobreak >nul

dotnet build WuwaChecklist.csproj -c Release
if errorlevel 1 (
  echo.
  echo 编译失败。
  exit /b 1
)

echo.
echo 好了：%~dp0..\src\bin\Release\WuwaChecklist.exe
endlocal
