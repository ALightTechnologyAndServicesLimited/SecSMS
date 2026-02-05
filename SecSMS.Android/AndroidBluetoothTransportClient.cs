using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Bluetooth;
using Java.Util;
using SecSMS.Common.Transport;

namespace SecSMS.Android;

public sealed class AndroidBluetoothTransportClient : ITransportClient
{
    readonly BluetoothDevice _device;
    readonly UUID _serviceUuid;

    BluetoothSocket? _socket;
    Stream? _input;
    Stream? _output;
    CancellationTokenSource? _receiveCts;
    bool _disposed;

    public AndroidBluetoothTransportClient(BluetoothDevice device, UUID serviceUuid)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _serviceUuid = serviceUuid ?? throw new ArgumentNullException(nameof(serviceUuid));
    }

    public TransportType TransportType => TransportType.Bluetooth;

    public string Name => string.IsNullOrWhiteSpace(_device.Name) ? _device.Address ?? "Bluetooth device" : _device.Name;

    public bool IsConnected => _socket?.IsConnected == true;

    public event EventHandler<TransportEnvelope>? MessageReceived;

    public event EventHandler<Exception>? Error;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (IsConnected)
        {
            return;
        }

        var adapter = BluetoothAdapter.DefaultAdapter ?? throw new InvalidOperationException("Bluetooth adapter not available.");

        if (!adapter.IsEnabled)
        {
            throw new InvalidOperationException("Bluetooth is disabled.");
        }

        _socket = _device.CreateRfcommSocketToServiceRecord(_serviceUuid);

        try
        {
            if (adapter.IsDiscovering)
            {
                adapter.CancelDiscovery();
            }

            await Task.Run(() => _socket.Connect(), cancellationToken).ConfigureAwait(false);

            _input = _socket.InputStream;
            _output = _socket.OutputStream;

            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _ = Task.Run(() => ReceiveLoopAsync(_receiveCts.Token));
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, ex);
            await DisconnectAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _receiveCts?.Cancel();
        }
        catch
        {
            // ignored
        }

        _receiveCts?.Dispose();
        _receiveCts = null;

        try
        {
            _input?.Dispose();
        }
        catch
        {
            // ignored
        }

        try
        {
            _output?.Dispose();
        }
        catch
        {
            // ignored
        }

        try
        {
            _socket?.Close();
        }
        catch
        {
            // ignored
        }

        _input = null;
        _output = null;
        _socket = null;

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task SendAsync(TransportEnvelope message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!IsConnected || _output == null)
        {
            throw new InvalidOperationException("Bluetooth connection is not established.");
        }

        var payload = message.Payload ?? Array.Empty<byte>();
        var header = new byte[5];
        header[0] = (byte)message.Type;
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(1, 4), payload.Length);

        await _output.WriteAsync(header, 0, header.Length, cancellationToken).ConfigureAwait(false);

        if (payload.Length > 0)
        {
            await _output.WriteAsync(payload, 0, payload.Length, cancellationToken).ConfigureAwait(false);
        }

        await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectAsync().ConfigureAwait(false);
    }

    async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_input == null)
        {
            return;
        }

        var header = new byte[5];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!await ReadExactAsync(_input, header, 0, header.Length, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                var type = (TransportMessageType)header[0];
                var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1, 4));

                if (length < 0)
                {
                    throw new InvalidDataException("Negative payload length received over Bluetooth.");
                }

                var payload = new byte[length];

                if (length > 0)
                {
                    var ok = await ReadExactAsync(_input, payload, 0, length, cancellationToken).ConfigureAwait(false);
                    if (!ok)
                    {
                        break;
                    }
                }

                MessageReceived?.Invoke(this, new TransportEnvelope(type, payload));
            }
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Error?.Invoke(this, ex);
            }
        }
    }

    static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AndroidBluetoothTransportClient));
        }
    }
}
