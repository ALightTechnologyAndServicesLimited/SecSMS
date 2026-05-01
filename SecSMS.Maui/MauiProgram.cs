using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SecSMS.Common;

namespace SecSMS.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if ANDROID
            builder.Services.AddSingleton<ISmsRepository>(_ => new AndroidSmsRepository(global::Android.App.Application.Context.ContentResolver));
            builder.Services.AddSingleton<IOtpTransferService, AndroidBluetoothOtpTransferService>();
#else
            builder.Services.AddSingleton<ISmsRepository, UnsupportedSmsRepository>();
            builder.Services.AddSingleton<IOtpTransferService, UnsupportedOtpTransferService>();
#endif

            builder.Services.AddTransient<MainPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
