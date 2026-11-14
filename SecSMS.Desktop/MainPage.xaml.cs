using System.Text;
using SecSMS.Common;
using SecSMS.Common.Totp;
using SecSMS.Common.Transport;

namespace SecSMS.Desktop
{
    public partial class MainPage : ContentPage
    {
        const int DefaultClipboardClearSeconds = 30;
        int _clipboardClearSeconds = DefaultClipboardClearSeconds;
        int _remainingSeconds;
        bool _timerRunning;
        bool _showCountdown = true;

#if WINDOWS
        WindowsBluetoothTransportServer? _bluetoothServer;
        CryptHelper? _cryptoHelper;
        PqKeyBundle? _androidKeyBundle;
#endif

        public MainPage()
        {
            InitializeComponent();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();

            _clipboardClearSeconds = Preferences.Get("ClipboardClearSeconds", DefaultClipboardClearSeconds);
            _showCountdown = Preferences.Get("ShowCountdown", true);

            CountdownContainer.IsVisible = false;
            CountdownLabel.Text = string.Empty;
            StatusLabel.Text = string.Empty;
            BluetoothStatusLabel.Text = "Not started";
            BluetoothLogEditor.Text = string.Empty;

            // Localize tab and link texts
            //SmsTabButton.Text = DesktopStrings.Get("Tab_Sms");
            //LinksTabButton.Text = DesktopStrings.Get("Tab_Links");
            //LinksIntroLabel.Text = DesktopStrings.Get("Links_Intro");
            Website1Label.Text = DesktopStrings.Get("Link1_Title");
            Website2Label.Text = DesktopStrings.Get("Link2_Title");
            Website3Label.Text = DesktopStrings.Get("Link3_Title");
        }

        async void OnCopyClicked(object? sender, EventArgs e)
        {
            var text = SmsEditor.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                StatusLabel.Text = "No SMS text to copy.";
                return;
            }

            await Clipboard.SetTextAsync(text);
            StatusLabel.Text = "SMS copied to clipboard.";

            _clipboardClearSeconds = Preferences.Get("ClipboardClearSeconds", DefaultClipboardClearSeconds);
            _showCountdown = Preferences.Get("ShowCountdown", true);

            if (_clipboardClearSeconds <= 0)
            {
                // No auto-clear configured
                CountdownContainer.IsVisible = false;
                CountdownLabel.Text = string.Empty;
                return;
            }

            _remainingSeconds = _clipboardClearSeconds;

            if (_showCountdown)
            {
                CountdownContainer.IsVisible = true;
                CountdownLabel.Text = $"{_remainingSeconds}s";
            }
            else
            {
                CountdownContainer.IsVisible = false;
            }

            if (!_timerRunning)
            {
                _timerRunning = true;
                Dispatcher.StartTimer(TimeSpan.FromSeconds(1), () =>
                {
                    if (_remainingSeconds <= 0)
                    {
                        _ = ClearClipboardAndUiAsync();
                        _timerRunning = false;
                        return false;
                    }

                    _remainingSeconds--;
                    if (_showCountdown && CountdownContainer.IsVisible)
                    {
                        CountdownLabel.Text = $"{_remainingSeconds}s";
                    }

                    return true;
                });
            }
        }

        async Task ClearClipboardAndUiAsync()
        {
            try
            {
                await Clipboard.SetTextAsync(string.Empty);
            }
            catch
            {
                // ignore clipboard errors
            }

            SmsEditor.Text = string.Empty;
            CountdownContainer.IsVisible = false;
            CountdownLabel.Text = string.Empty;
            StatusLabel.Text = "Clipboard cleared.";
        }

        void OnSmsTabClicked(object? sender, System.EventArgs e)
        {
            SmsTabContent.IsVisible = true;
            LinksTabContent.IsVisible = false;
        }

        void OnLinksTabClicked(object? sender, System.EventArgs e)
        {
            SmsTabContent.IsVisible = false;
            LinksTabContent.IsVisible = true;
        }

        async void OnWebsite1Tapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
        {
            await OpenUrlAsync("https://www.simplepro.site");
        }

        async void OnWebsite2Tapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
        {
            await OpenUrlAsync("https://simplepass.alightservices.com");
        }

        async void OnWebsite3Tapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
        {
            await OpenUrlAsync("https://webveta.alightservices.comm");
        }

        async void OnWebsite4Tapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
        {
            await OpenUrlAsync("https://vpn.alightservices.com");
        }

        async Task OpenUrlAsync(string url)
        {
            try
            {
                await Microsoft.Maui.ApplicationModel.Launcher.OpenAsync(new System.Uri(url));
            }
            catch (System.Exception ex)
            {
                StatusLabel.Text = "Failed to open link.";
                AppendBluetoothLog("OpenUrlAsync failed: " + ex);
            }
        }

        void OnStartBluetoothClicked(object? sender, EventArgs e)
        {
#if WINDOWS
            if (_bluetoothServer != null)
            {
                BluetoothStatusLabel.Text = "Bluetooth already listening";
                AppendBluetoothLog("StartBluetoothClicked: server already listening.");
                return;
            }

            StartBluetoothServer();
#else
            BluetoothStatusLabel.Text = "Bluetooth only available on Windows";
            AppendBluetoothLog("StartBluetoothClicked on non-Windows platform.");
#endif
        }

        void OnSendPqKeyBundleClicked(object? sender, EventArgs e)
        {
#if WINDOWS
            if (_bluetoothServer == null || !_bluetoothServer.IsConnected)
            {
                StatusLabel.Text = "Bluetooth is not connected.";
                AppendBluetoothLog("OnSendPqKeyBundleClicked: no active Bluetooth connection.");
                return;
            }

            try
            {
                _cryptoHelper ??= new CryptHelper();
                var keyBundle = _cryptoHelper.CreatePqKeyBundle();
                var payload = Encoding.UTF8.GetBytes(CryptHelper.SerializePqKeyBundle(keyBundle));
                var envelope = new TransportEnvelope(TransportMessageType.PqKeyBundle, payload);

                _ = _bluetoothServer.SendAsync(envelope);

                StatusLabel.Text = "PQ key bundle sent to mobile.";
                AppendBluetoothLog($"Sent PQ key bundle ({payload.Length} bytes).");
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Failed to send PQ key bundle.";
                AppendBluetoothLog("OnSendPqKeyBundleClicked error: " + ex);
            }
#else
            StatusLabel.Text = "PQ key sending is only available on Windows.";
            AppendBluetoothLog("OnSendPqKeyBundleClicked on non-Windows platform.");
#endif
        }

        async void OnSendTotpFromClipboardClicked(object? sender, EventArgs e)
        {
#if WINDOWS
            if (_bluetoothServer == null || !_bluetoothServer.IsConnected)
            {
                StatusLabel.Text = "Bluetooth is not connected. On Android, open TOTP and tap 'From Desktop' first.";
                return;
            }

            if (_androidKeyBundle == null)
            {
                StatusLabel.Text = "No Android key bundle yet. On Android, open TOTP and tap 'From Desktop' first.";
                return;
            }

            string? clip = null;
            try
            {
                clip = (await Clipboard.GetTextAsync())?.Trim();
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Could not read the clipboard.";
                AppendBluetoothLog("Clipboard read failed: " + ex);
                return;
            }

            if (string.IsNullOrWhiteSpace(clip))
            {
                StatusLabel.Text = "Clipboard is empty. Copy the otpauth:// URI or the Base32 secret first.";
                return;
            }

            // Validate without persisting anything. The raw value is never shown in
            // the UI or logs; only issuer/account (or a masked placeholder) appear.
            string maskedTarget;
            if (clip.StartsWith("otpauth://", StringComparison.OrdinalIgnoreCase))
            {
                if (!OtpauthParser.TryParse(clip, out var parsed, out var parseError) || parsed == null)
                {
                    StatusLabel.Text = "Clipboard is not a valid TOTP enrollment: " + parseError;
                    return;
                }

                maskedTarget = string.IsNullOrWhiteSpace(parsed.DisplayName)
                    ? "an unnamed TOTP account (you can name it on the phone)"
                    : $"'{parsed.DisplayName}'";
            }
            else
            {
                try
                {
                    // Throws for anything that is not valid Base32. Result discarded.
                    _ = TotpGenerator.Generate(clip, DateTimeOffset.UtcNow);
                }
                catch
                {
                    StatusLabel.Text = "Clipboard is neither an otpauth:// URI nor a valid Base32 TOTP secret.";
                    return;
                }

                maskedTarget = "a bare TOTP secret (you'll name it on the phone)";
            }

            var confirm = await DisplayAlert(
                "Send TOTP to Android",
                $"Send {maskedTarget} to Android? The secret stays hidden and the clipboard is cleared after sending.",
                "Send",
                "Cancel");
            if (!confirm)
            {
                return;
            }

            try
            {
                _cryptoHelper ??= new CryptHelper();
                var encrypted = _cryptoHelper.EncryptPq(clip, _androidKeyBundle);
                var payload = Encoding.UTF8.GetBytes(CryptHelper.SerializePqEncryptedMessage(encrypted));
                await _bluetoothServer.SendAsync(new TransportEnvelope(TransportMessageType.EncryptedTotpEnrollment, payload));

                try
                {
                    await Clipboard.SetTextAsync(string.Empty);
                }
                catch
                {
                    // Clipboard clear is best-effort; never fail the send over it.
                }

                // Android disconnects after one import; require a fresh handshake
                // for the next send so we never "send" into a closed socket.
                _androidKeyBundle = null;
                StatusLabel.Text = "Encrypted TOTP enrollment sent. Confirm on Android. To send another, tap 'From Desktop' on Android again.";
                AppendBluetoothLog($"Sent encrypted TOTP enrollment ({payload.Length} bytes).");
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Failed to send TOTP enrollment.";
                AppendBluetoothLog("TOTP enrollment send failed: " + ex);
            }
#else
            StatusLabel.Text = "TOTP clipboard import is only available on Windows.";
            await Task.CompletedTask;
#endif
        }

#if WINDOWS
        async void StartBluetoothServer()
        {
            BluetoothStatusLabel.Text = "Starting Bluetooth listener...";
            AppendBluetoothLog("Starting Bluetooth server listener.");
            _bluetoothServer = new WindowsBluetoothTransportServer();
            _bluetoothServer.MessageReceived += OnBluetoothMessageReceived;
            _bluetoothServer.Error += OnBluetoothError;

            try
            {
                await _bluetoothServer.ConnectAsync();
                BluetoothStatusLabel.Text = "Listening for Bluetooth connections";
                AppendBluetoothLog("Bluetooth server is now listening for connections.");
            }
            catch (Exception ex)
            {
                BluetoothStatusLabel.Text = "Bluetooth start failed";
                StatusLabel.Text = ex.Message;
                AppendBluetoothLog("StartBluetoothServer failed: " + ex);
                await _bluetoothServer.DisposeAsync();
                _bluetoothServer = null;
            }
        }

        void OnBluetoothError(object? sender, Exception ex)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BluetoothStatusLabel.Text = "Bluetooth error";
                StatusLabel.Text = ex.Message;
                AppendBluetoothLog("Bluetooth error event: " + ex);
            });
        }

        void OnBluetoothMessageReceived(object? sender, TransportEnvelope envelope)
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BluetoothStatusLabel.Text = "Connected";
                AppendBluetoothLog($"Message received: Type={envelope.Type}, Length={envelope.Payload.Length}.");

                if (envelope.Type == TransportMessageType.EncryptedOtp)
                {
                    if (_cryptoHelper == null)
                    {
                        StatusLabel.Text = "Encrypted OTP received but PQ keys are not initialized.";
                        return;
                    }

                    try
                    {
                        var encryptedPayload = Encoding.UTF8.GetString(envelope.Payload);
                        var encryptedMessage = CryptHelper.DeserializePqEncryptedMessage(encryptedPayload);
                        var otp = _cryptoHelper.DecryptPq(encryptedMessage);
                        SmsEditor.Text = otp;
                        StatusLabel.Text = "Decrypted OTP received over Bluetooth.";
                    }
                    catch (Exception ex)
                    {
                        StatusLabel.Text = "Failed to decrypt OTP.";
                        AppendBluetoothLog("DecryptPq failed: " + ex);
                    }
                }
                else if (envelope.Type == TransportMessageType.PqKeyBundle)
                {
                    try
                    {
                        // Android's public bundle: lets us encrypt a TOTP enrollment
                        // that only the Android device can decrypt.
                        _androidKeyBundle = CryptHelper.DeserializePqKeyBundle(Encoding.UTF8.GetString(envelope.Payload));
                        StatusLabel.Text = "Android is ready to receive a TOTP enrollment from the clipboard.";
                    }
                    catch (Exception ex)
                    {
                        StatusLabel.Text = "Failed to read Android key bundle.";
                        AppendBluetoothLog("DeserializePqKeyBundle failed: " + ex);
                    }
                }
                else
                {
                    StatusLabel.Text = $"BT message received: {envelope.Type} ({envelope.Payload.Length} bytes)";
                }
            });
        }

        protected override void OnDisappearing()
        {
            base.OnDisappearing();
            if (_bluetoothServer != null)
            {
                _ = _bluetoothServer.DisposeAsync();
                _bluetoothServer = null;
                AppendBluetoothLog("Bluetooth server disposed on page disappearing.");
            }
        }
#endif

        void AppendBluetoothLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var line = $"[{timestamp}] {message}";
            if (string.IsNullOrWhiteSpace(BluetoothLogEditor.Text))
            {
                BluetoothLogEditor.Text = line;
            }
            else
            {
                BluetoothLogEditor.Text += Environment.NewLine + line;
            }
        }
    }
}
