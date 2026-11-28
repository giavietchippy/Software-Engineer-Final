@echo off
cd /d "%~dp0\.."
set DOTNET_ROLL_FORWARD=Major
echo Starting SaigonBite... Press Ctrl+C to stop. If you see "Now listening on", it is NOT crashed.
dotnet run --project SaigonBite.Web
exit /b %errorlevel%