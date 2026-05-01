using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using SecSMS.Common;
using SecSMS.Maui.Localization;

namespace SecSMS.Maui;

public partial class MessageDetailPage : ContentPage
{
    readonly string _messageId;
    readonly ISmsRepository _smsRepository;
    readonly IOtpTransferService _otpTransferService;
    readonly List<string> _otpCandidates = new();

    string? _selectedOtp;
    string? _preferredOtp;
    string? _selectedUrl;
    string? _rawBody;
    bool _loaded;

    public MessageDetailPage(string messageId, ISmsRepository smsRepository, IOtpTransferService otpTransferService)
    {
        InitializeComponent();
        _messageId = messageId;
        _smsRepository = smsRepository;
        _otpTransferService = otpTransferService;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_loaded)
        {
            return;
        }

        _loaded = true;
        await LoadMessageAsync();
    }

    async Task LoadMessageAsync()
    {
        try
        {
            var detail = await _smsRepository.GetMessageByIdAsync(_messageId);
            if (detail == null)
            {
                await DisplayAlert("Error", LocalizationService.NoMessages, LocalizationService.OK);
                await Navigation.PopAsync();
                return;
            }

            var maskedBody = SmsMasking.MaskSensitiveText(detail.Body);
            _preferredOtp = SmsMasking.ExtractOtp(detail.Body);
            var otpCandidates = SmsMasking.ExtractAllOtps(detail.Body).Distinct().ToList();
            _selectedUrl = SmsMasking.ExtractAllUrls(detail.Body).FirstOrDefault();
            _rawBody = detail.Body;

            if (!string.IsNullOrEmpty(_preferredOtp))
            {
                var index = otpCandidates.IndexOf(_preferredOtp);
                if (index > 0)
                {
                    otpCandidates.RemoveAt(index);
                    otpCandidates.Insert(0, _preferredOtp);
                }
                else if (index < 0 && otpCandidates.Count > 0)
                {
                    otpCandidates.Insert(0, _preferredOtp);
                }
            }

            _otpCandidates.Clear();
            _otpCandidates.AddRange(otpCandidates);
            _selectedOtp = _preferredOtp ?? (_otpCandidates.Count > 0 ? _otpCandidates[0] : null);

            FromNumberLabel.Text = detail.FromNumber;
            SimSlotLabel.Text = detail.SimSlot.ToString();
            ReceivedAtLabel.Text = detail.ReceivedAt.LocalDateTime.ToString();
            BodyLabel.Text = maskedBody;
            OpenInBrowserButton.IsVisible = !string.IsNullOrWhiteSpace(_selectedUrl);

            if (_otpCandidates.Count > 1)
            {
                OtpPicker.ItemsSource = _otpCandidates.Select(value => new string('*', value.Length)).ToList();
                OtpPicker.SelectedIndex = Math.Max(0, _otpCandidates.IndexOf(_selectedOtp ?? _otpCandidates[0]));
                OtpPicker.IsVisible = true;
            }
            else
            {
                OtpPicker.IsVisible = false;
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", ex.Message, LocalizationService.OK);
            await Navigation.PopAsync();
        }
    }

    async void OnBackClicked(object? sender, EventArgs e)
    {
        await Navigation.PopAsync();
    }

    void OnOtpSelectionChanged(object? sender, EventArgs e)
    {
        var selectedIndex = OtpPicker.SelectedIndex;
        if (selectedIndex >= 0 && selectedIndex < _otpCandidates.Count)
        {
            _selectedOtp = _otpCandidates[selectedIndex];
        }
    }

    async void OnCopyClicked(object? sender, EventArgs e)
    {
        var textToCopy = _selectedOtp ?? _preferredOtp ?? _selectedUrl ?? _rawBody ?? string.Empty;
        if (string.IsNullOrWhiteSpace(textToCopy))
        {
            await DisplayAlert(LocalizationService.CopyOtp, LocalizationService.NothingToCopy, LocalizationService.OK);
            return;
        }

        await Clipboard.Default.SetTextAsync(textToCopy);
        await DisplayAlert(LocalizationService.CopyOtp, LocalizationService.CopiedToClipboard, LocalizationService.OK);
    }

    async void OnSendClicked(object? sender, EventArgs e)
    {
        var textToSend = _selectedOtp ?? _preferredOtp ?? _selectedUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(textToSend))
        {
            await DisplayAlert(LocalizationService.SendToWindows, LocalizationService.NothingToSend, LocalizationService.OK);
            return;
        }

        try
        {
            SendToWindowsButton.IsEnabled = false;
            await _otpTransferService.SendAsync(textToSend);
            await DisplayAlert(LocalizationService.SendToWindows, LocalizationService.EncryptedOtpSent, LocalizationService.OK);
        }
        catch (OperationCanceledException)
        {
            await DisplayAlert(LocalizationService.SendToWindows, LocalizationService.BluetoothCancelled, LocalizationService.OK);
        }
        catch (Exception ex)
        {
            await DisplayAlert(LocalizationService.SendToWindows, ex.Message, LocalizationService.OK);
        }
        finally
        {
            SendToWindowsButton.IsEnabled = true;
        }
    }

    async void OnOpenInBrowserClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedUrl))
        {
            await DisplayAlert(LocalizationService.OpenInBrowser, LocalizationService.NoUrlFound, LocalizationService.OK);
            return;
        }

        try
        {
            await Browser.Default.OpenAsync(_selectedUrl, BrowserLaunchMode.SystemPreferred);
        }
        catch (Exception)
        {
            await DisplayAlert(LocalizationService.OpenInBrowser, LocalizationService.UnableToOpenLink, LocalizationService.OK);
        }
    }
}
