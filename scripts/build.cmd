@echo off
cd /d "%~dp0\.."
dotnet build SaigonBite.sln
if errorlevel 1 exit /b 1
echo Build succeeded.