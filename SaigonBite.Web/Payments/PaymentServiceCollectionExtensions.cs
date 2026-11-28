using Microsoft.Extensions.Configuration;
using SaigonBite.Core.Payments;

namespace SaigonBite.Web.Payments;

public static class PaymentServiceCollectionExtensions
{
    public static IServiceCollection AddSaigonBitePaymentStrategies(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PaymentGatewayOptions>(configuration.GetSection(PaymentGatewayOptions.Section));

        services.AddSingleton<VnPayGateway>();
        services.AddHttpClient<PayPalGateway>();
        services.AddHttpClient<MomoGateway>(client =>
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "SaigonBite/1.0");
        });

        var mode = configuration[$"{PaymentGatewayOptions.Section}:Mode"] ?? "Mock";
        var live = string.Equals(mode, "Live", StringComparison.OrdinalIgnoreCase);

        if (live)
        {
            services.AddSingleton<IPaymentStrategy, LiveMomoPayment>();
            services.AddSingleton<IPaymentStrategy, LiveVnPayPayment>();
            services.AddSingleton<IPaymentStrategy, LiveApplePayPayment>();
            services.AddSingleton<IPaymentStrategy, LivePayPalPayment>();
        }
        else
        {
            services.AddSingleton<IPaymentStrategy, MomoPayment>();
            services.AddSingleton<IPaymentStrategy, VnPayPayment>();
            services.AddSingleton<IPaymentStrategy, ApplePayPayment>();
            services.AddSingleton<IPaymentStrategy, PayPalPayment>();
        }

        services.AddSingleton<IPaymentStrategy, CodPayment>();
        services.AddSingleton<PaymentContext>();
        return services;
    }
}