using SecSMS.Common.Totp;

namespace SecSMS.Maui;

public partial class TotpManualEntryPage : ContentPage
{
    readonly ITotpRepository _totpRepository;
    readonly string[] _algorithms = ["SHA1", "SHA256", "SHA512"];
    readonly string[] _digits = ["6", "7", "8"];

    public TotpManualEntryPage(ITotpRepository totpRepository)
    {
        InitializeComponent();
        _totpRepository = totpRepository;
        AlgorithmPicker.ItemsSource = _algorithms.ToList();
        AlgorithmPicker.SelectedIndex = 0;
        DigitsPicker.ItemsSource = _digits.ToList();
        DigitsPicker.SelectedIndex = 0;
    }

    async void OnSaveClicked(object? sender, EventArgs e)
    {
        var issuer = IssuerEntry.Text?.Trim() ?? string.Empty;
        var account = AccountEntry.Text?.Trim() ?? string.Empty;
        var secret = SecretEntry.Text?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(secret))
        {
            await DisplayAlert("TOTP", "Secret is required.", "OK");
            return;
        }

        var algorithm = AlgorithmPicker.SelectedIndex switch
        {
            1 => TotpAlgorithm.Sha256,
            2 => TotpAlgorithm.Sha512,
            _ => TotpAlgorithm.Sha1,
        };

        var digits = DigitsPicker.SelectedIndex switch
        {
            1 => 7,
            2 => 8,
            _ => 6,
        };

        var entry = new TotpEntry(Guid.NewGuid().ToString("N"), issuer, account, secret, algorithm, digits, 30);

        try
        {
            // Validates Base32 + test-generates a code (in-memory only) before saving.
            _ = TotpGenerator.Generate(entry, DateTimeOffset.UtcNow);
            await _totpRepository.AddAsync(entry);
        }
        catch (Exception ex)
        {
            await DisplayAlert("TOTP", "Invalid details: " + ex.Message, "OK");
            return;
        }

        await DisplayAlert("TOTP", $"Added '{entry.DisplayName}'.", "OK");
        await Navigation.PopAsync();
    }

    async void OnCancelClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }
}
