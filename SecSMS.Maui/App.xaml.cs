using Microsoft.Extensions.DependencyInjection;
using SecSMS.Maui.Services;

namespace SecSMS.Maui
{
    public partial class App : Application
    {
        readonly IServiceProvider _serviceProvider;
        Window? _window;
        bool _lockShown;

        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            _serviceProvider = serviceProvider;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new NavigationPage(_serviceProvider.GetRequiredService<MainPage>()));
            _window = window;

            // Re-lock when the app goes to background; re-prompt when foregrounded.
            window.Deactivated += (_, _) => _serviceProvider.GetRequiredService<IAppLockService>().Lock();
            window.Activated += (_, _) => _ = ShowLockIfNeededAsync();
            window.Created += (_, _) => _ = ShowLockIfNeededAsync();

            return window;
        }

        async Task ShowLockIfNeededAsync()
        {
            if (_lockShown)
            {
                return;
            }

            var appLock = _serviceProvider.GetRequiredService<IAppLockService>();
            var navigation = _window?.Page?.Navigation;
            if (appLock.IsUnlocked || navigation == null || navigation.ModalStack.Count > 0)
            {
                return;
            }

            _lockShown = true;
            try
            {
                await navigation.PushModalAsync(
                    _serviceProvider.GetRequiredService<LockPage>());
            }
            finally
            {
                // PushModalAsync completes once shown; a second modal is
                // prevented by the ModalStack check above.
                _lockShown = false;
            }
        }

        /// <summary>Called by MainPage manual lock button.</summary>
        public Task ShowLockAsync()
        {
            return ShowLockIfNeededAsync();
        }
    }
}
