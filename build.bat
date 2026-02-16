@echo off
echo === SysBot.ACNHOrders Build ===
echo.

dotnet publish "%~dp0SysBot.ACNHOrders.csproj" -c Release -f net10.0 -o "%~dp0publish"

echo.
if %ERRORLEVEL% EQU 0 (
    echo Build erfolgreich! Dateien in: %~dp0publish
) else (
    echo Build fehlgeschlagen!
)
echo.
pause
