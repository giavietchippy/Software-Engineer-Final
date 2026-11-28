@echo off
setlocal
cd /d "%~dp0\.."
set DOTNET_ROLL_FORWARD=Major
echo === Build ===
dotnet build SaigonBite.sln -v minimal || exit /b 1
echo === Database ===
dotnet ef database update --project SaigonBite.Web --startup-project SaigonBite.Web --no-build || exit /b 1
echo === Payment tests ===
dotnet test SaigonBite.sln -v minimal --no-build --filter "FullyQualifiedName~PaymentTests|FullyQualifiedName~Allowed_payment|FullyQualifiedName~Checkout_lists_only_BR13" || exit /b 1
echo.
echo All checks passed.
exit /b 0