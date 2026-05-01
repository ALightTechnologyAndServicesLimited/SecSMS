using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Widget;
using Java.Util;
using Microsoft.Maui.ApplicationModel;
using SecSMS.Common;
using SecSMS.Common.Transport;
using SecSMS.Maui.Localization;

namespace SecSMS.Maui;

public sealed class AndroidBluetoothOtpTransferService : IOtpTransferService
{
    static readonly UUID BluetoothServiceUuid = UUID.FromString("00001101-0000-1000-8000-00805F9B34FB");

    public async Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("Nothing to send.");
        }

        var adapter = BluetoothAdapter.DefaultAdapter;
        if (adapter == null)
        {
            throw new InvalidOperationException("Bluetooth is not available on this device.");
        }

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Please enable Bluetooth and try again.");
        }

        if (!await AppPermissions.EnsureBluetoothPermissionsAsync())
        {
            throw new InvalidOperationException("Bluetooth permission is required.");
        }

        var bondedDevices = adapter.BondedDevices;
        if (bondedDevices == null || bondedDevices.Count == 0)
        {
            throw new InvalidOperationException("No paired Bluetooth devices found.");
        }

        var devices = new List<BluetoothDevice>(bondedDevices);
        var selectedDevice = await SelectDeviceAsync(devices);
        if (selectedDevice == null)
        {
            throw new OperationCanceledException("Bluetooth device selection was canceled.");
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

        await using var client = new AndroidBluetoothTransportClient(selectedDevice, BluetoothServiceUuid);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cryptoHelper = new CryptHelper();

        void OnError(object? sender, Exception ex)
        {
            completion.TrySetException(ex);
        }

        void OnMessageReceived(object? sender, TransportEnvelope envelope)
        {
            if (envelope.Type != TransportMessageType.PqKeyBundle)
            {
                return;
            }

            try
            {
                var keyBundlePayload = Encoding.UTF8.GetString(envelope.Payload);
                var keyBundle = CryptHelper.DeserializePqKeyBundle(keyBundlePayload);
                var encryptedMessage = cryptoHelper.EncryptPq(text, keyBundle);
                var payload = Encoding.UTF8.GetBytes(CryptHelper.SerializePqEncryptedMessage(encryptedMessage));
                var response = new TransportEnvelope(TransportMessageType.EncryptedOtp, payload);
                _ = SendResponseAsync(client, response, completion, timeoutCts.Token);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }

        client.Error += OnError;
        client.MessageReceived += OnMessageReceived;

        try
        {
            using var registration = timeoutCts.Token.Register(() => completion.TrySetCanceled(timeoutCts.Token));
            await client.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);

            var activity = Platform.CurrentActivity;
            if (activity != null)
            {
                activity.RunOnUiThread(() =>
                {
                    Toast.MakeText(activity, LocalizationService.WaitingForPqBundle, ToastLength.Short)?.Show();
                });
            }

            await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out waiting for the desktop key bundle.");
        }
        finally
        {
            client.Error -= OnError;
            client.MessageReceived -= OnMessageReceived;
        }
    }

    static async Task SendResponseAsync(AndroidBluetoothTransportClient client, TransportEnvelope response, TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        await client.SendAsync(response, cancellationToken).ConfigureAwait(false);
        completion.TrySetResult();
    }

    static Task<BluetoothDevice?> SelectDeviceAsync(IReadOnlyList<BluetoothDevice> devices)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("No active Android activity is available.");
        var completion = new TaskCompletionSource<BluetoothDevice?>(TaskCreationOptions.RunContinuationsAsynchronously);

        activity.RunOnUiThread(() =>
        {
            var names = new string[devices.Count];
            for (var i = 0; i < devices.Count; i++)
            {
                var device = devices[i];
                names[i] = string.IsNullOrWhiteSpace(device.Name) ? device.Address : device.Name;
            }

            var builder = new AlertDialog.Builder(activity);
            builder.SetTitle("Select Windows device");
            builder.SetItems(names, (_, args) => completion.TrySetResult(devices[args.Which]));
            builder.SetOnCancelListener(new CancelListener(() => completion.TrySetResult(null)));
            builder.SetNegativeButton("Cancel", (_, _) => completion.TrySetResult(null));
            builder.Show();
        });

        return completion.Task;
    }

    sealed class CancelListener : Java.Lang.Object, IDialogInterfaceOnCancelListener
    {
        readonly Action _onCancel;

        public CancelListener(Action onCancel)
        {
            _onCancel = onCancel;
        }

        public void OnCancel(IDialogInterface? dialog)
        {
            _onCancel();
        }
    }
}
