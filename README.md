# Software-Engineer-Final
This is the project of SaigonBite

# SaigonBite core (pricing, payment Strategy, order state machine)

Pure C# class library with no web or database code, so the ASP.NET MVC project only references it.

| File | Covers |
|---|---|
| Pricing/PricingService.cs | BR1 to BR11 (BR10 calculation order) |
| Pricing/HubLoad.cs | BR8 load definition |
| Orders/OrderLineValidator.cs | BR12 |
| Orders/OrderStateMachine.cs | BR14, BR15, BR16 |
| Payments/* | BR13, BR14 (Strategy pattern) |

## Create the solution and run the tests
```
dotnet new sln -n SaigonBite
dotnet sln add SaigonBite.Core/SaigonBite.Core.csproj SaigonBite.Tests/SaigonBite.Tests.csproj
dotnet test
```
If package versions in SaigonBite.Tests.csproj do not restore, create the test project with
`dotnet new xunit -n SaigonBite.Tests`, copy the four *Tests.cs files into it, and add a project reference to SaigonBite.Core.

## Register in ASP.NET Core (Program.cs)
```csharp
builder.Services.AddSingleton<PricingService>();
builder.Services.AddSingleton<IPaymentStrategy, MomoPayment>();
builder.Services.AddSingleton<IPaymentStrategy, VnPayPayment>();
builder.Services.AddSingleton<IPaymentStrategy, ApplePayPayment>();
builder.Services.AddSingleton<IPaymentStrategy, PayPalPayment>();
builder.Services.AddSingleton<IPaymentStrategy, CodPayment>();
builder.Services.AddSingleton<PaymentContext>();   // receives all IPaymentStrategy registrations
```
A new payment method = one new class + one new registration line (Change Request evidence).
