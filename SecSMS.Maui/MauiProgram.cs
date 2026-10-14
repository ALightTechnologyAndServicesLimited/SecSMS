using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Biometric;
using SecSMS.Common;
using SecSMS.Common.Totp;
using SecSMS.Maui.Services;
using SecSMS.Maui.Totp;
using ZXing.Net.Maui.Controls;

namespace SecSMS.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseBarcodeReader()
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

            builder.Services.AddSingleton<ITotpRepository, SecureStorageTotpRepository>();
            builder.Services.AddSingleton<IBiometric>(BiometricAuthenticationService.Default);
            builder.Services.AddSingleton<IAppLockService, AppLockService>();

            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<LockPage>();
            builder.Services.AddTransient<TotpListPage>();
            builder.Services.AddTransient<QrScannerPage>();
            builder.Services.AddTransient<TotpManualEntryPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
