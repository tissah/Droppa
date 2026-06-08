using CommunityToolkit.Maui;
using Droppa.Services;
using Droppa.ViewModels;
using Droppa.Views;
using Microsoft.Extensions.Logging;

namespace Droppa;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseMauiMaps()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        RegisterServices(builder.Services);
        RegisterViewModelsAndPages(builder.Services);

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // HTTP + API client talking to the Droppa backend.
        services.AddSingleton(new HttpClient { BaseAddress = new Uri(Services.Api.ApiConfig.BaseUrl) });
        services.AddSingleton<Services.Api.ITokenStore, Services.Api.TokenStore>();
        services.AddSingleton<Services.Api.DroppaApiClient>();

        // API-backed services.
        services.AddSingleton<IAuthService, ApiAuthService>();
        services.AddSingleton<ICourierRepository, ApiCourierService>();
        services.AddSingleton<IBookingService, ApiBookingService>();

        // Mock payment gateway (Airtel Money / TNM Mpamba / Visa). Swap for a real one later.
        services.AddSingleton<IPaymentService, MockPaymentService>();

        // Local device services.
        services.AddSingleton<ILocationService, LocationService>();
        services.AddSingleton<IDistanceService, HaversineDistanceService>(); // straight-line estimate for receive

        // Realtime tracking over SignalR (driver location, ride accepted, status changes).
        services.AddSingleton<ITrackingService, SignalRTrackingService>();

        // Google Directions API (its own HttpClient — the shared one is bound to the Droppa API).
        services.AddSingleton<Services.Maps.IDirectionsService>(_ =>
            new Services.Maps.GoogleDirectionsService(new HttpClient()));
    }

    private static void RegisterViewModelsAndPages(IServiceCollection services)
    {
        services.AddSingleton<LoginViewModel>();
        services.AddSingleton<LoginPage>();

        services.AddTransient<RegisterViewModel>();
        services.AddTransient<RegisterPage>();

        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<HomePage>();

        // One payment panel VM per parcel flow.
        services.AddTransient<PaymentViewModel>();

        services.AddTransient<SendParcelViewModel>();
        services.AddTransient<SendParcelPage>();

        services.AddTransient<ReceiveParcelViewModel>();
        services.AddTransient<ReceiveParcelPage>();

        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<HistoryPage>();

        // Customer live tracking.
        services.AddTransient<TrackDeliveryViewModel>();
        services.AddTransient<TrackDeliveryPage>();

        // Driver screens.
        services.AddTransient<DriverJobsViewModel>();
        services.AddTransient<DriverJobsPage>();

        services.AddTransient<DriverTripsViewModel>();
        services.AddTransient<DriverTripsPage>();

        services.AddTransient<DriverJobPreviewViewModel>();
        services.AddTransient<DriverJobPreviewPage>();

        services.AddTransient<DriverDeliveryViewModel>();
        services.AddTransient<DriverDeliveryPage>();
    }
}
