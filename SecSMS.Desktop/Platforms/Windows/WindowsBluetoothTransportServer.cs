using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using SecSMS.Common.Transport;

namespace SecSMS.Desktop;

public sealed class WindowsBluetoothTransportServer : ITransportClient
{
    readonly Guid _serviceGuid;

    RfcommServiceProvider? _provider;
    StreamSocketListener? _listener;
    StreamSocket? _socket;
    DataReader? _reader;
    DataWriter? _writer;
    CancellationTokenSource? _receiveCts;

    public WindowsBluetoothTransportServer()
        : this(new Guid("00001101-0000-1000-8000-00805F9B34FB"))
    {
    }

    public WindowsBluetoothTransportServer(Guid serviceGuid)
    {
        _serviceGuid = serviceGuid;
    }

    public TransportType TransportType => TransportType.Bluetooth;

    public string Name => "Windows Bluetooth Server";

    public bool IsConnected => _socket != null;

    public event EventHandler<TransportEnvelope>? MessageReceived;

    public event EventHandler<Exception>? Error;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_provider != null)
        {
            // Already listening
            return;
        }

        var serviceId = RfcommServiceId.FromUuid(_serviceGuid);
        _provider = await RfcommServiceProvider.CreateAsync(serviceId).AsTask(cancellationToken).ConfigureAwait(false);

        _listener = new StreamSocketListener();
        _listener.ConnectionReceived += OnConnectionReceived;

        await _listener.BindServiceNameAsync(_provider.ServiceId.AsString()).AsTask(cancellationToken).ConfigureAwait(false);

        _provider.StartAdvertising(_listener);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _receiveCts?.Cancel();
        _receiveCts = null;

        try
        {
            _reader?.DetachStream();
        }
        catch
        {
        }

        _reader?.Dispose();
        _reader = null;

        try
        {
            _writer?.DetachStream();
        }
        catch
        {
        }

        _writer?.Dispose();
        _writer = null;

        _socket?.Dispose();
        _socket = null;

        if (_provider != null)
        {
            _provider.StopAdvertising();
            _provider = null;
        }

        if (_listener != null)
        {
            _listener.ConnectionReceived -= OnConnectionReceived;
            _listener.Dispose();
            _listener = null;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async Task SendAsync(TransportEnvelope message, CancellationToken cancellationToken = default)
    {
        if (_writer == null)
        {
            throw new InvalidOperationException("No active Bluetooth connection.");
        }

        var payload = message.Payload ?? Array.Empty<byte>();

        _writer.ByteOrder = ByteOrder.BigEndian;
        _writer.WriteByte((byte)message.Type);
        _writer.WriteInt32(payload.Length);
        if (payload.Length > 0)
        {
            _writer.WriteBytes(payload);
        }

        await _writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }

    void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args)
    {
        // Accept a single active connection; replace any existing socket.
        _socket?.Dispose();
        _socket = args.Socket;

        _reader?.Dispose();
        _writer?.Dispose();

        _reader = new DataReader(_socket.InputStream)
        {
            ByteOrder = ByteOrder.BigEndian
        };
        _writer = new DataWriter(_socket.OutputStream)
        {
            ByteOrder = ByteOrder.BigEndian
        };

        _receiveCts?.Cancel();
        _receiveCts = new CancellationTokenSource();
        _ = ReceiveLoopAsync(_receiveCts.Token);
    }

    async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _reader;
        if (reader == null)
        {
            return;
        }

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var loaded = await reader.LoadAsync(5).AsTask(cancellationToken).ConfigureAwait(false);
                if (loaded < 5)
                {
                    break;
                }

                var type = (TransportMessageType)reader.ReadByte();
                var length = reader.ReadInt32();
                if (length < 0)
                {
                    throw new InvalidOperationException("Negative payload length received over Bluetooth.");
                }

                var payload = new byte[length];
                if (length > 0)
                {
                    loaded = await reader.LoadAsync((uint)length).AsTask(cancellationToken).ConfigureAwait(false);
                    if (loaded < length)
                    {
                        break;
                    }

                    reader.ReadBytes(payload);
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
}
