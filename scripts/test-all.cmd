@echo off
cd /d "%~dp0\.."
set DOTNET_ROLL_FORWARD=Major
dotnet test SaigonBite.sln -v minimal
if errorlevel 1 (
  echo.
  echo TESTS FAILED - see red errors above.
  exit /b 1
)
echo.
echo ALL TESTS PASSED - Failed: 0
exit /b 0