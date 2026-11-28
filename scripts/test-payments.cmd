@echo off
cd /d "%~dp0\.."
dotnet test SaigonBite.sln -v minimal --filter "FullyQualifiedName~PaymentTests|FullyQualifiedName~Allowed_payment|FullyQualifiedName~Checkout_lists_only_BR13"
exit /b %errorlevel%