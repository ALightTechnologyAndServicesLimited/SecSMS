using SecSMS.Common.Totp;
using ZXing.Net.Maui;

namespace SecSMS.Maui;

public partial class QrScannerPage : ContentPage
{
    readonly ITotpRepository _totpRepository;
    bool _handled;

    public QrScannerPage(ITotpRepository totpRepository)
    {
        InitializeComponent();
        _totpRepository = totpRepository;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _handled = false;
        BarcodeReader.IsDetecting = true;
        BarcodeReader.Options = new BarcodeReaderOptions
        {
            Formats = BarcodeFormats.TwoDimensional,
            AutoRotate = true,
            TryHarder = true,
        };
    }

    protected override void OnDisappearing()
    {
        BarcodeReader.IsDetecting = false;
        base.OnDisappearing();
    }

    void OnTorchClicked(object? sender, EventArgs e)
    {
        BarcodeReader.IsTorchOn = !BarcodeReader.IsTorchOn;
    }

    async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    async void OnBarcodesDetected(object? sender, BarcodeDetectionEventArgs e)
    {
        if (_handled)
        {
            return;
        }

        var raw = e.Results?.FirstOrDefault()?.Value;
        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
        {
            // Ignore non-TOTP QR codes; keep scanning.
            return;
        }

        _handled = true;
        BarcodeReader.IsDetecting = false;

        if (!OtpauthParser.TryParse(raw, out var entry, out var error) || entry == null)
        {
            await MainThread.InvokeOnMainThreadAsync(() => DisplayAlert("TOTP", error ?? "Invalid QR code.", "OK"));
            await MainThread.InvokeOnMainThreadAsync(() => Navigation.PopAsync());
            return;
        }

        // Some QR codes carry neither a label nor an issuer. Ask for names so the
        // account is recognizable in the list instead of saving an unnamed entry.
        if (string.IsNullOrWhiteSpace(entry.Issuer) && string.IsNullOrWhiteSpace(entry.AccountName))
        {
            var issuer = await MainThread.InvokeOnMainThreadAsync(() => DisplayPromptAsync("Name account", "Issuer (e.g. GitHub)", "OK", "Cancel", "Issuer", -1, Keyboard.Text, string.Empty));
            if (issuer == null)
            {
                await MainThread.InvokeOnMainThreadAsync(() => Navigation.PopAsync());
                return;
            }

            var account = await MainThread.InvokeOnMainThreadAsync(() => DisplayPromptAsync("Name account", "Account (e.g. user@example.com)", "OK", "Cancel", "Account", -1, Keyboard.Text, string.Empty));
            if (account == null)
            {
                await MainThread.InvokeOnMainThreadAsync(() => Navigation.PopAsync());
                return;
            }

            entry.Issuer = issuer.Trim();
            entry.AccountName = account.Trim();
            if (string.IsNullOrWhiteSpace(entry.Issuer) && string.IsNullOrWhiteSpace(entry.AccountName))
            {
                await MainThread.InvokeOnMainThreadAsync(() => DisplayAlert("TOTP", "Enter at least an issuer or an account name.", "OK"));
                await MainThread.InvokeOnMainThreadAsync(() => Navigation.PopAsync());
                return;
            }
        }

        try
        {
            await _totpRepository.AddAsync(entry);
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => DisplayAlert("TOTP", "Could not save account: " + ex.Message, "OK"));
            await MainThread.InvokeOnMainThreadAsync(() => Navigation.PopAsync());
            return;
        }

        // Never show the secret or a generated code here.
        await MainThread.InvokeOnMainThreadAsync(() => DisplayAlert("TOTP", $"Added '{entry.DisplayName}'.", "OK"));
        await MainThread.InvokeOnMainThreadAsync(() => Navigation.PopAsync());
    }
}
