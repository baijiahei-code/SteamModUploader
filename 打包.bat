@echo off
chcp 65001 >nul
setlocal
title Steam MOD 上传工具 - 一键打包

set "ROOT=%~dp0"

echo ============================================
echo   Steam MOD 上传工具 - 一键打包
echo ============================================
echo.

REM ---- 1. 发布单文件绿色版 ----
echo [1/2] 正在发布单文件绿色版（publish\win-x64）...
dotnet publish "%ROOT%SteamModUploader\SteamModUploader.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o "%ROOT%publish\win-x64"
if errorlevel 1 (
    echo.
    echo [错误] 发布失败，请检查上面的输出。
    pause
    exit /b 1
)
echo [完成] 单文件绿色版已生成：publish\win-x64\SteamModUploader.exe
echo.

REM ---- 2. 查找 ISCC.exe（Inno Setup 6）----
set "ISCC="
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "C:\Program Files\Inno Setup 6\ISCC.exe" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"

if not defined ISCC (
    echo.
    echo [错误] 未找到 Inno Setup 6（ISCC.exe），请先安装：https://jrsoftware.org/isdl.php
    echo        单文件版已生成，但安装程序未编译。
    pause
    exit /b 1
)

echo [2/2] 正在编译安装程序（ISCC.exe）...
"%ISCC%" "%ROOT%installer\installer.iss"
if errorlevel 1 (
    echo.
    echo [错误] 编译安装程序失败，请检查上面的输出。
    pause
    exit /b 1
)

echo.
echo ============================================
echo   打包完成！
echo   单文件版：  publish\win-x64\SteamModUploader.exe
echo   安装程序：  publish\SteamModUploader-Setup.exe
echo ============================================
pause
