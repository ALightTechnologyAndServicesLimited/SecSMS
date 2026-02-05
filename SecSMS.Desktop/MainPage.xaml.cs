using System.Text;
using SecSMS.Common;
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
            await OpenUrlAsync("https://www.simpleprro.site");
        }

        async void OnWebsite2Tapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
        {
            await OpenUrlAsync("https://simplepass.alightservices.com");
        }

        async void OnWebsite3Tapped(object? sender, Microsoft.Maui.Controls.TappedEventArgs e)
        {
            await OpenUrlAsync("https://webvveta.alightservices.comm");
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

        void OnSendRsaPublicKeyClicked(object? sender, EventArgs e)
        {
#if WINDOWS
            if (_bluetoothServer == null || !_bluetoothServer.IsConnected)
            {
                StatusLabel.Text = "Bluetooth is not connected.";
                AppendBluetoothLog("OnSendRsaPublicKeyClicked: no active Bluetooth connection.");
                return;
            }

            try
            {
                _cryptoHelper ??= new CryptHelper();
                _cryptoHelper.GenerateRSAKeyPair();
                var publicKey = _cryptoHelper.GetPublicKey();
                var payload = Encoding.UTF8.GetBytes(publicKey);
                var envelope = new TransportEnvelope(TransportMessageType.RsaPublicKey, payload);

                _ = _bluetoothServer.SendAsync(envelope);

                StatusLabel.Text = "RSA public key sent to mobile.";
                AppendBluetoothLog($"Sent RSA public key ({payload.Length} bytes).");
            }
            catch (Exception ex)
            {
                StatusLabel.Text = "Failed to send RSA public key.";
                AppendBluetoothLog("OnSendRsaPublicKeyClicked error: " + ex);
            }
#else
            StatusLabel.Text = "RSA key sending is only available on Windows.";
            AppendBluetoothLog("OnSendRsaPublicKeyClicked on non-Windows platform.");
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
                        StatusLabel.Text = "Encrypted OTP received but RSA keys are not initialized.";
                        return;
                    }

                    try
                    {
                        var cipherText = Encoding.UTF8.GetString(envelope.Payload);
                        var otp = _cryptoHelper.DecryptRSA(cipherText);
                        SmsEditor.Text = otp;
                        StatusLabel.Text = "Decrypted OTP received over Bluetooth.";
                    }
                    catch (Exception ex)
                    {
                        StatusLabel.Text = "Failed to decrypt OTP.";
                        AppendBluetoothLog("DecryptRSA failed: " + ex);
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
