@echo off
chcp 65001 >nul
setlocal
title Steam MOD 上传工具 - 一键打包

set "ROOT=%~dp0"

REM ---- 0. 读取版本号（统一从 csproj 取，避免多处维护）----
set "APPVER="
for /f "usebackq delims=" %%i in (`powershell -NoProfile -Command "([xml](Get-Content -Raw '%ROOT%SteamModUploader\SteamModUploader.csproj')).Project.PropertyGroup.Version"`) do set "APPVER=%%i"
if not defined APPVER set "APPVER=0.0.0"

echo ============================================
echo   Steam MOD 上传工具 - 一键打包
echo ============================================
echo.
echo 版本号：%APPVER%
echo.

REM ---- 1. 发布单文件绿色版 ----
echo [1/2] 正在发布单文件绿色版（publish\win-x64）...
REM 先清空旧产物：否则之前版本残留的卫星资源目录（cs/de/ja...）会一直留着
if exist "%ROOT%publish\win-x64" rmdir /s /q "%ROOT%publish\win-x64"
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
"%ISCC%" /DMyAppVersion=%APPVER% "%ROOT%installer\installer.iss"
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
