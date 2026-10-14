using System;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using SecSMS.Common;
using SecSMS.Maui.Localization;
using SecSMS.Maui.Services;

namespace SecSMS.Maui;

public partial class MainPage : ContentPage
{
    readonly ISmsRepository _smsRepository;
    readonly IServiceProvider _serviceProvider;
    readonly IAppLockService _appLock;
    readonly ObservableCollection<SmsMessageSummary> _items = new();

    bool _loaded;
    bool _updatingLockSwitch;

    public MainPage(ISmsRepository smsRepository, IServiceProvider serviceProvider, IAppLockService appLock)
    {
        InitializeComponent();
        _smsRepository = smsRepository;
        _serviceProvider = serviceProvider;
        _appLock = appLock;
        MessagesCollectionView.ItemsSource = _items;
        ShowMessagesTab();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        RefreshLockUi();

        if (_loaded)
        {
            return;
        }

        _loaded = true;

        try
        {
            await LoadMessagesAsync();
        }
        catch (Exception ex)
        {
            ShowEmpty($"Failed to load messages: {ex.Message}");
        }
    }

    async Task LoadMessagesAsync()
    {
        try
        {
            ShowLoading();

#if ANDROID
            if (!await AppPermissions.EnsureSmsReadPermissionAsync())
            {
                ShowEmpty(LocalizationService.PermissionRequired);
                return;
            }
#endif

            var messages = await _smsRepository.GetRecentMessagesAsync(TimeSpan.FromMinutes(120));

            _items.Clear();
            foreach (var message in messages)
            {
                _items.Add(message);
            }

            if (_items.Count == 0)
            {
                ShowEmpty(LocalizationService.NoMessages);
            }
            else
            {
                ShowList();
            }
        }
        catch (Exception ex)
        {
            ShowEmpty(ex.Message);
        }
        finally
        {
            MessagesRefreshView.IsRefreshing = false;
        }
    }

    void ShowLoading()
    {
        LoadingIndicator.IsVisible = true;
        LoadingIndicator.IsRunning = true;
        MessagesCollectionView.IsVisible = false;
        EmptyLabel.IsVisible = false;
    }

    void ShowEmpty(string message)
    {
        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;
        MessagesCollectionView.IsVisible = false;
        EmptyLabel.Text = message;
        EmptyLabel.IsVisible = true;
    }

    void ShowList()
    {
        LoadingIndicator.IsVisible = false;
        LoadingIndicator.IsRunning = false;
        EmptyLabel.IsVisible = false;
        MessagesCollectionView.IsVisible = true;
    }

    void ShowMessagesTab()
    {
        MessagesContent.IsVisible = true;
        LinksContent.IsVisible = false;
    }

    void ShowLinksTab()
    {
        MessagesContent.IsVisible = false;
        LinksContent.IsVisible = true;
    }

    void OnMessagesTabClicked(object? sender, EventArgs e)
    {
        ShowMessagesTab();
    }

    void OnLinksTabClicked(object? sender, EventArgs e)
    {
        ShowLinksTab();
    }

    async void OnTotpTabClicked(object? sender, EventArgs e)
    {
        var page = _serviceProvider.GetService<TotpListPage>()
            ?? ActivatorUtilities.CreateInstance<TotpListPage>(_serviceProvider);
        await Navigation.PushAsync(page);
    }

    void RefreshLockUi()
    {
        _updatingLockSwitch = true;
        try
        {
            BiometricLockSwitch.IsToggled = _appLock.IsLockEnabled;
            BiometricStatusLabel.Text = _appLock.IsLockEnabled
                ? "App locks on background. Unlock with a Strong biometric (fingerprint, or certified Class 3 face)."
                : "App lock is off. Tap Lock above or turn on the switch to protect messages and codes.";
            if (_appLock.IsLockEnabled)
            {
                _ = AppendBiometricDetailsAsync();
            }
        }
        finally
        {
            _updatingLockSwitch = false;
        }
    }

    async Task AppendBiometricDetailsAsync()
    {
        try
        {
            var details = await _appLock.GetAvailabilityDetailsAsync();
            BiometricStatusLabel.Text += " " + details;
        }
        catch
        {
            // Status line is informational only; never crash the page for it.
        }
    }

    async void OnLockClicked(object? sender, EventArgs e)
    {
        if (!_appLock.IsLockEnabled)
        {
            // One-tap path: enroll + lock immediately instead of dead-ending
            // with "turn it on below first".
            if (!await _appLock.IsBiometricAvailableAsync())
            {
                var details = await _appLock.GetAvailabilityDetailsAsync();
                var open = await DisplayAlert("Lock", details + " Open system settings to enroll?", "Open settings", "Cancel");
                if (open)
                {
                    _appLock.TryOpenBiometricSettings();
                }
                return;
            }

            if (!await _appLock.AuthenticateAsync("Enable app lock"))
            {
                return;
            }

            _appLock.SetLockEnabled(true);
            RefreshLockUi();
        }

        _appLock.Lock();
        if (Application.Current is App app)
        {
            await app.ShowLockAsync();
        }
    }

    async void OnBiometricLockToggled(object? sender, ToggledEventArgs e)
    {
        if (_updatingLockSwitch)
        {
            return;
        }

        if (e.Value)
        {
            // Prove face/fingerprint works before enabling, or the user
            // could lock themselves out on a device without enrollment.
            if (!await _appLock.IsBiometricAvailableAsync())
            {
                await DisplayAlert("Lock", "Face/fingerprint is not set up on this device.", "OK");
                RefreshLockUi();
                return;
            }

            var ok = await _appLock.AuthenticateAsync("Enable app lock");
            if (!ok)
            {
                RefreshLockUi();
                return;
            }

            _appLock.SetLockEnabled(true);
        }
        else
        {
            // Require proof to disable, or anyone holding the phone turns lock off.
            var ok = await _appLock.AuthenticateAsync("Disable app lock");
            if (!ok)
            {
                RefreshLockUi();
                return;
            }

            _appLock.SetLockEnabled(false);
        }

        RefreshLockUi();
    }

    async void OnRefreshRequested(object? sender, EventArgs e)
    {
        await LoadMessagesAsync();
    }

    async void OnMessageSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.Count == 0 || e.CurrentSelection[0] is not SmsMessageSummary item)
        {
            return;
        }

        MessagesCollectionView.SelectedItem = null;

        var page = ActivatorUtilities.CreateInstance<MessageDetailPage>(_serviceProvider, item.Id);
        await Navigation.PushAsync(page);
    }

    async void OnLinkClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string url || string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
            await DisplayAlert(LocalizationService.TabLinks, LocalizationService.UnableToOpenLink, "OK");
        }
    }
}
