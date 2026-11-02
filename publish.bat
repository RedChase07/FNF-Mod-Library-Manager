@echo off
setlocal
cd /d "%~dp0"

dotnet publish ".\FnFModLibrary.WinUI.csproj" -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -o ".\publish\win-x64"
if errorlevel 1 (
    echo.
    echo Publish failed. Review the error above.
    exit /b 1
)

echo.
echo Standalone release created in:
echo %~dp0publish\win-x64
