using SecSMS.Maui.Services;

namespace SecSMS.Maui;

public partial class LockPage : ContentPage
{
    readonly IAppLockService _appLock;
    bool _prompting;

    public LockPage(IAppLockService appLock)
    {
        InitializeComponent();
        _appLock = appLock;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Auto-prompt once so the user just sees face/fingerprint immediately.
        await PromptAsync(auto: true);
    }

    protected override bool OnBackButtonPressed()
    {
        // Stay locked: block hardware back while the app is locked.
        return true;
    }

    async void OnUnlockClicked(object? sender, EventArgs e)
    {
        await PromptAsync(auto: false);
    }

    async void OnOpenSettingsClicked(object? sender, EventArgs e)
    {
        if (!_appLock.TryOpenBiometricSettings())
        {
            await DisplayAlert("Locked", "Open system Settings → Security → Fingerprint / Face to enroll, then return here and tap Unlock.", "OK");
        }
    }

    async void OnDisableLockClicked(object? sender, EventArgs e)
    {
        // Escape hatch: if the device has no biometrics enrolled there is no
        // hardware boundary to enforce, so let the user opt out explicitly
        // rather than bricking the app. Only offered when biometric auth
        // is unavailable (button is hidden otherwise).
        _appLock.SetLockEnabled(false);
        await Navigation.PopModalAsync();
    }

    async Task PromptAsync(bool auto)
    {
        if (_prompting)
        {
            return;
        }

        _prompting = true;
        UnlockButton.IsEnabled = false;
        try
        {
            if (!await _appLock.IsBiometricAvailableAsync())
            {
                StatusLabel.Text = await _appLock.GetAvailabilityDetailsAsync();
                OpenSettingsButton.IsVisible = true;
                DisableLockButton.IsVisible = true;
                return;
            }

            var ok = await _appLock.AuthenticateAsync("Unlock SecSMS");
            if (ok)
            {
                await Navigation.PopModalAsync();
            }
            else if (!auto)
            {
                StatusLabel.Text = "Not recognized. Try again.";
            }
        }
        finally
        {
            _prompting = false;
            UnlockButton.IsEnabled = true;
        }
    }
}
