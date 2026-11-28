@echo off
cd /d "%~dp0\.."
set DOTNET_ROLL_FORWARD=Major
dotnet ef database update --project SaigonBite.Web --startup-project SaigonBite.Web
exit /b %errorlevel%