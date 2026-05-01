using System;
using System.Collections.ObjectModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using SecSMS.Common;
using SecSMS.Maui.Localization;

namespace SecSMS.Maui;

public partial class MainPage : ContentPage
{
    readonly ISmsRepository _smsRepository;
    readonly IServiceProvider _serviceProvider;
    readonly ObservableCollection<SmsMessageSummary> _items = new();

    bool _loaded;

    public MainPage(ISmsRepository smsRepository, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _smsRepository = smsRepository;
        _serviceProvider = serviceProvider;
        MessagesCollectionView.ItemsSource = _items;
        ShowMessagesTab();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

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
