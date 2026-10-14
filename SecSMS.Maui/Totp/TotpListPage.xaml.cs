using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SecSMS.Common.Totp;

namespace SecSMS.Maui;

public partial class TotpListPage : ContentPage
{
    readonly ITotpRepository _totpRepository;
    readonly IOtpTransferService _otpTransferService;
    readonly ObservableCollection<TotpRow> _rows = new();
    readonly Dictionary<string, TotpEntry> _entriesById = new();

    bool _timerRunning;

    public TotpListPage(ITotpRepository totpRepository, IOtpTransferService otpTransferService)
    {
        InitializeComponent();
        _totpRepository = totpRepository;
        _otpTransferService = otpTransferService;
        TotpCollectionView.ItemsSource = _rows;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadAsync();
        StartCountdownTimer();
    }

    async Task LoadAsync()
    {
        try
        {
            var entries = await _totpRepository.ListAsync().ConfigureAwait(true);
            _entriesById.Clear();
            _rows.Clear();
            foreach (var entry in entries.OrderBy(e => e.DisplayName))
            {
                _entriesById[entry.Id] = entry;
                _rows.Add(new TotpRow(entry));
            }

            EmptyLabel.IsVisible = _rows.Count == 0;
            UpdateCountdown();
        }
        catch (Exception ex)
        {
            await DisplayAlert("TOTP", "Failed to load accounts: " + ex.Message, "OK");
        }
    }

    void StartCountdownTimer()
    {
        if (_timerRunning)
        {
            return;
        }

        _timerRunning = true;
        Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
        {
            if (!IsVisible)
            {
                return true;
            }

            UpdateCountdown();
            return true;
        });
    }

    void UpdateCountdown()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var row in _rows)
        {
            row.Refresh(now);
        }
    }

    TotpEntry? FindEntry(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return _entriesById.TryGetValue(id, out var entry) ? entry : null;
    }

    async void OnScanQrClicked(object? sender, EventArgs e)
    {
        if (!await AppPermissions.EnsureCameraPermissionAsync())
        {
            await DisplayAlert("TOTP", "Camera permission is required to scan QR codes.", "OK");
            return;
        }

        await Navigation.PushAsync(new QrScannerPage(_totpRepository));
    }

    async void OnManualEntryClicked(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new TotpManualEntryPage(_totpRepository));
    }

    async void OnRefreshClicked(object? sender, EventArgs e)
    {
        await LoadAsync();
    }

    async void OnCopyClicked(object? sender, EventArgs e)
    {
        var id = (sender as Button)?.CommandParameter as string;
        var entry = FindEntry(id);
        if (entry == null)
        {
            return;
        }

        string? code = null;
        try
        {
            // Generate in-memory only; never assign to a Label.
            code = TotpGenerator.Generate(entry, DateTimeOffset.UtcNow);
            await Clipboard.Default.SetTextAsync(code);
            await DisplayAlert("TOTP", "Code copied to clipboard.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("TOTP", "Copy failed: " + ex.Message, "OK");
        }
    }

    async void OnSendClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        var entry = FindEntry(button.CommandParameter as string);
        if (entry == null)
        {
            return;
        }

        string? code = null;
        try
        {
            button.IsEnabled = false;
            // Generate in-memory only; sent over existing PQ-encrypted BT channel.
            code = TotpGenerator.Generate(entry, DateTimeOffset.UtcNow);
            await _otpTransferService.SendAsync(code);
            await DisplayAlert("TOTP", "Encrypted code sent to Windows.", "OK");
        }
        catch (OperationCanceledException)
        {
            await DisplayAlert("TOTP", "Bluetooth send was cancelled.", "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("TOTP", "Send failed: " + ex.Message, "OK");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    async void OnRenameClicked(object? sender, EventArgs e)
    {
        var id = (sender as Button)?.CommandParameter as string;
        var entry = FindEntry(id);
        if (entry == null)
        {
            return;
        }

        // Secret, algorithm, digits and period are never editable here —
        // only the display labels. Re-enroll to change anything else.
        var issuer = await DisplayPromptAsync("Rename", "Issuer (e.g. GitHub)", "OK", "Cancel", "Issuer", -1, Keyboard.Text, entry.Issuer);
        if (issuer == null)
        {
            return;
        }

        var account = await DisplayPromptAsync("Rename", "Account (e.g. user@example.com)", "OK", "Cancel", "Account", -1, Keyboard.Text, entry.AccountName);
        if (account == null)
        {
            return;
        }

        issuer = issuer.Trim();
        account = account.Trim();
        if (string.IsNullOrWhiteSpace(issuer) && string.IsNullOrWhiteSpace(account))
        {
            await DisplayAlert("TOTP", "Enter at least an issuer or an account name.", "OK");
            return;
        }

        if (string.Equals(issuer, entry.Issuer, StringComparison.Ordinal) &&
            string.Equals(account, entry.AccountName, StringComparison.Ordinal))
        {
            return;
        }

        entry.Issuer = issuer;
        entry.AccountName = account;

        try
        {
            // AddAsync upserts by Id and re-validates the secret.
            await _totpRepository.AddAsync(entry);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("TOTP", "Rename failed: " + ex.Message, "OK");
        }
    }

    async void OnDeleteClicked(object? sender, EventArgs e)
    {
        var id = (sender as Button)?.CommandParameter as string;
        var entry = FindEntry(id);
        if (entry == null)
        {
            return;
        }

        var confirm = await DisplayAlert("TOTP", $"Delete '{entry.DisplayName}'?", "Delete", "Cancel");
        if (!confirm)
        {
            return;
        }

        await _totpRepository.DeleteAsync(entry.Id);
        await LoadAsync();
    }

    sealed class TotpRow : INotifyPropertyChanged
    {
        readonly TotpEntry _entry;
        string _countdownText = string.Empty;
        double _progress;

        public TotpRow(TotpEntry entry)
        {
            _entry = entry;
            Refresh(DateTimeOffset.UtcNow);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id => _entry.Id;

        public string DisplayName => string.IsNullOrWhiteSpace(_entry.DisplayName) ? "(unnamed)" : _entry.DisplayName;

        public string Meta => $"{_entry.Algorithm.ToString().ToUpperInvariant()} · {_entry.Digits} digits · every {_entry.Period}s";

        public string CountdownText
        {
            get => _countdownText;
            private set => SetField(ref _countdownText, value);
        }

        public double Progress
        {
            get => _progress;
            private set => SetField(ref _progress, value);
        }

        public void Refresh(DateTimeOffset utcNow)
        {
            var remaining = TotpGenerator.GetRemainingSeconds(utcNow, _entry.Period);
            CountdownText = $"expires in {remaining}s";
            Progress = (double)remaining / _entry.Period;
        }

        void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
