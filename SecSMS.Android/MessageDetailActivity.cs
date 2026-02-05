using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Widget;
using Java.Util;
using SecSMS.Common;
using SecSMS.Common.Transport;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SecSMS.Android;

[Activity(Label = "@string/message_detail_title")]
public sealed class MessageDetailActivity : Activity
{
    public const string ExtraMessageId = "message_id";

    AndroidSmsRepository? _smsRepository;
    TextView? _fromView;
    TextView? _simView;
    TextView? _timeView;
    TextView? _bodyView;
    Spinner? _otpSpinner;
    Button? _backButton;
    Button? _copyButton;
    Button? _sendButton;
    AndroidBluetoothTransportClient? _bluetoothClient;
    string? _pendingOtp;
    string? _selectedOtp;
    CryptHelper? _cryptoHelper;

    static readonly string[] BluetoothPermissions =
        {
            Manifest.Permission.BluetoothConnect,
            Manifest.Permission.BluetoothScan,
        };

    static readonly UUID BluetoothServiceUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");
    const int RequestBluetoothPermissionsCode = 1002;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        SetContentView(Resource.Layout.activity_message_detail);

        _smsRepository = new AndroidSmsRepository(ContentResolver);

        _fromView = FindViewById<TextView>(Resource.Id.fromNumberDetailTextView);
        _simView = FindViewById<TextView>(Resource.Id.simSlotDetailTextView);
        _timeView = FindViewById<TextView>(Resource.Id.receivedAtDetailTextView);
        _bodyView = FindViewById<TextView>(Resource.Id.bodyDetailTextView);
        _otpSpinner = FindViewById<Spinner>(Resource.Id.otpSpinner);
        _backButton = FindViewById<Button>(Resource.Id.backButton);
        _copyButton = FindViewById<Button>(Resource.Id.copyOtpButton);
        _sendButton = FindViewById<Button>(Resource.Id.sendToWindowsButton);

        if (_backButton != null)
        {
            _backButton.Click += (_, _) => Finish();
        }

        var messageId = Intent?.GetStringExtra(ExtraMessageId);
        if (string.IsNullOrEmpty(messageId))
        {
            Toast.MakeText(this, "No message selected.", ToastLength.Short).Show();
            Finish();
            return;
        }

        _ = LoadMessageAsync(messageId);
    }

    async Task LoadMessageAsync(string messageId)
    {
        if (_smsRepository == null)
        {
            return;
        }

        try
        {
            var detail = await _smsRepository.GetMessageByIdAsync(messageId);
            if (detail == null)
            {
                Toast.MakeText(this, "Message not found.", ToastLength.Short).Show();
                Finish();
                return;
            }

            var maskedBody = SmsMasking.MaskDigits(detail.Body);
            var otp = SmsMasking.ExtractOtp(detail.Body);
            var otpCandidates = SmsMasking.ExtractAllOtps(detail.Body).Distinct().ToList();
            _selectedOtp = otp;

            if (_fromView != null)
            {
                _fromView.Text = detail.FromNumber;
            }

            if (_simView != null)
            {
                _simView.Text = detail.SimSlot.ToString();
            }

            if (_timeView != null)
            {
                _timeView.Text = detail.ReceivedAt.LocalDateTime.ToString();
            }

            if (_bodyView != null)
            {
                _bodyView.Text = maskedBody;
            }

            if (_otpSpinner != null)
            {
                if (otpCandidates.Count > 1)
                {
                    _otpSpinner.Visibility = global::Android.Views.ViewStates.Visible;
                    // Show masked representations in the dropdown, but keep the real values in otpCandidates
                    var displayItems = otpCandidates.Select(v => new string('*', v.Length)).ToList();
                    var adapter = new ArrayAdapter<string>(this, Resource.Layout.otp_spinner_item);
                    foreach (var item in displayItems)
                    {
                        adapter.Add(item);
                    }
                    adapter.SetDropDownViewResource(Resource.Layout.otp_spinner_dropdown_item);
                    _otpSpinner.Adapter = adapter;

                    var initialIndex = _selectedOtp != null ? otpCandidates.IndexOf(_selectedOtp) : -1;
                    if (initialIndex < 0 && otpCandidates.Count > 0)
                    {
                        initialIndex = 0;
                    }
                    if (initialIndex >= 0)
                    {
                        _otpSpinner.SetSelection(initialIndex);
                        _selectedOtp = otpCandidates[initialIndex];
                    }

                    _otpSpinner.ItemSelected += (s, e) =>
                    {
                        if (e.Position >= 0 && e.Position < otpCandidates.Count)
                        {
                            _selectedOtp = otpCandidates[e.Position];
                        }
                    };
                }
                else
                {
                    _otpSpinner.Visibility = global::Android.Views.ViewStates.Gone;
                }
            }

            if (_copyButton != null)
            {
                _copyButton.Click += (_, _) => CopyOtpToClipboard(_selectedOtp ?? otp ?? maskedBody);
            }

            if (_sendButton != null)
            {
                _sendButton.Click += (_, _) => SendOTP(_selectedOtp ?? otp ?? maskedBody);
            }
        }
        catch (Exception ex)
        {
            //Android.Util.Log.Error("SecSMS", $"Error loading SMS detail: {ex}");
            Toast.MakeText(this, "Unable to load message.", ToastLength.Short).Show();
        }
    }

    private void SendOTP(string otp)
    {
        if (string.IsNullOrWhiteSpace(otp))
        {
            Toast.MakeText(this, "No OTP to send.", ToastLength.Short).Show();
            return;
        }

        _pendingOtp = otp;
        BluetoothConnect();
    }

    void BluetoothConnect()
    {
        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter == null)
        {
            Toast.MakeText(this, "Bluetooth is not available on this device.", ToastLength.Long).Show();
            return;
        }

        if (!adapter.IsEnabled)
        {
            Toast.MakeText(this, "Please enable Bluetooth and try again.", ToastLength.Long).Show();
            return;
        }

        if (!HasBluetoothPermissions())
        {
            RequestPermissions(BluetoothPermissions, RequestBluetoothPermissionsCode);
            return;
        }

        var bondedDevices = adapter.BondedDevices;
        if (bondedDevices == null || bondedDevices.Count == 0)
        {
            Toast.MakeText(this, "No paired Bluetooth devices found.", ToastLength.Long).Show();
            return;
        }

        var devices = new List<BluetoothDevice>(bondedDevices);
        var names = new string[devices.Count];
        for (var i = 0; i < devices.Count; i++)
        {
            var d = devices[i];
            names[i] = string.IsNullOrWhiteSpace(d.Name) ? d.Address : d.Name;
        }

        var builder = new AlertDialog.Builder(this);
        builder.SetTitle("Select Windows device");
        builder.SetItems(names, (s, args) =>
        {
            var device = devices[args.Which];
            _ = ConnectToBluetoothDeviceAsync(device);
        });
        builder.SetNegativeButton("Cancel", (s, args) => { });
        builder.Show();
    }

    void OnBluetoothError(object? sender, Exception ex)
    {
        RunOnUiThread(() =>
        {
            Toast.MakeText(this, "Bluetooth error: " + ex.Message, ToastLength.Long).Show();
        });
    }

    void OnBluetoothMessageReceived(object? sender, TransportEnvelope envelope)
    {
        if (envelope.Type == TransportMessageType.RsaPublicKey)
        {
            try
            {
                var publicKey = Encoding.UTF8.GetString(envelope.Payload);

                _cryptoHelper ??= new CryptHelper();
                if (!_cryptoHelper.InitializeRSA(publicKey))
                {
                    RunOnUiThread(() =>
                    {
                        Toast.MakeText(this, "Failed to initialize RSA with received public key.", ToastLength.Long).Show();
                    });
                    return;
                }

                if (string.IsNullOrWhiteSpace(_pendingOtp))
                {
                    RunOnUiThread(() =>
                    {
                        Toast.MakeText(this, "No pending OTP to encrypt.", ToastLength.Short).Show();
                    });
                    return;
                }

                var encryptedText = _cryptoHelper.EncryptRSA(_pendingOtp);
                var payload = Encoding.UTF8.GetBytes(encryptedText ?? string.Empty);
                var response = new TransportEnvelope(TransportMessageType.EncryptedOtp, payload);

                if (_bluetoothClient != null)
                {
                    _ = _bluetoothClient.SendAsync(response);
                }

                RunOnUiThread(() =>
                {
                    Toast.MakeText(this, "Encrypted OTP sent to desktop.", ToastLength.Short).Show();
                });
            }
            catch (Exception ex)
            {
                RunOnUiThread(() =>
                {
                    Toast.MakeText(this, $"Error handling RSA public key: {ex.Message}", ToastLength.Long).Show();
                });
            }

            return;
        }

        RunOnUiThread(() =>
        {
            Toast.MakeText(this, $"BT message received ({envelope.Type}, {envelope.Payload.Length} bytes).", ToastLength.Short).Show();
        });
    }

    async Task ConnectToBluetoothDeviceAsync(BluetoothDevice device)
    {
        try
        {
            if (_bluetoothClient == null)
            {
                _bluetoothClient = new AndroidBluetoothTransportClient(device, BluetoothServiceUuid);
                _bluetoothClient.Error += OnBluetoothError;
                _bluetoothClient.MessageReceived += OnBluetoothMessageReceived;
            }

            await _bluetoothClient.ConnectAsync();

            RunOnUiThread(() =>
            {
                Toast.MakeText(this, $"Connected to {_bluetoothClient?.Name}. Waiting for RSA public key...", ToastLength.Short).Show();
            });
        }
        catch (Exception ex)
        {
            RunOnUiThread(() =>
            {
                Toast.MakeText(this, $"Bluetooth connect failed: {ex.Message}", ToastLength.Long).Show();
            });
        }
    }

    bool HasBluetoothPermissions()
    {
        // BLUETOOTH_CONNECT / BLUETOOTH_SCAN are runtime permissions on Android 12+
        if ((int)Build.VERSION.SdkInt < (int)BuildVersionCodes.S)
        {
            return true;
        }

        foreach (var permission in BluetoothPermissions)
        {
            if (CheckSelfPermission(permission) != Permission.Granted)
            {
                return false;
            }
        }

        return true;
    }

    void CopyOtpToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Toast.MakeText(this, "No OTP to copy.", ToastLength.Short).Show();
            return;
        }
        // most of the invisible spying equipment people are shameless
        // prostitutes and their pimps are announcing rates of their sluts

        var clipboard = (ClipboardManager?)GetSystemService(ClipboardService);
        if (clipboard == null)
        {
            Toast.MakeText(this, "Clipboard not available.", ToastLength.Short).Show();
            return;
        }

        //var clip = ClipData.NewPlainText("OTP", text);
        //clipboard.PrimaryClip.AddItem(clip.GetItemAt(0));// .SetPrimaryClip(clip);


        clipboard.Text = text;

        //Clipboard.SetTextAsync(text);

        Toast.MakeText(this, "OTP copied to clipboard.", ToastLength.Short).Show();
    }
}
