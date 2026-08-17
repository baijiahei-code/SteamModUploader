@echo off
REM ============================================================
REM  编译 Steam MOD 上传工具安装程序（需要 Inno Setup 6）
REM  下载: https://jrsoftware.org/isdl.php
REM ============================================================

set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo.
    echo [错误] 未找到 Inno Setup，请先安装:
    echo        https://jrsoftware.org/isdl.php
    echo.
    pause
    exit /b 1
)

echo 正在编译安装程序...
"%ISCC%" "%~dp0installer.iss"
echo.
echo 完成！安装程序输出到 ..\publish\SteamModUploader-Setup.exe
pause
