using CommunityToolkit.Maui;
using Inovesys.Infrastructure;
using Inovesys.Retail.Pages;
using Inovesys.Retail.Services;
using Microsoft.Extensions.Logging;
using Plugin.Maui.KeyListener;
using zoft.MauiExtensions.Controls;

namespace Inovesys.Retail;

public static class MauiProgramExtensions
{
	public static MauiAppBuilder UseSharedMauiApp(this MauiAppBuilder builder)
	{
        builder
     .UseMauiApp<App>()
        #if ANDROID || IOS || MACCATALYST || TIZEN || WINDOWS
                .UseMauiCommunityToolkit()
        #endif
     .UseZoftAutoCompleteEntry()
     .UseKeyListener()
     .ConfigureFonts(fonts =>
     {
         fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
         fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
         fonts.AddFont("RobotoMono-Regular.ttf", "Mono");
     });


        builder.Services.AddSingleton<HttpClient>(sp =>
        {
            var client = new HttpClient { BaseAddress = new Uri("https://api.inovesys.com.br/") };
            return client;
        });

        
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ConsumerSalePage>();
        builder.Services.AddTransient<CustomerRegistrationPage>();
        
        builder.Services.AddSingleton<LiteDbService>();
        builder.Services.AddSingleton<ProductRepositoryLiteDb>();
        builder.Services.AddSingleton<SyncService>();

        builder.Services.AddTransient<AuthHeaderHandler>();

        builder.Services.AddHttpClient("api", client =>
        {
            client.BaseAddress = new Uri("https://api.inovesys.com.br/"); // ← URL base fixa aqui
        })
        .AddHttpMessageHandler<AuthHeaderHandler>();


        builder.Services.AddSingleton<ToastService>();


#if DEBUG
        builder.Logging.AddDebug();
#endif

		return builder;
	}
}
