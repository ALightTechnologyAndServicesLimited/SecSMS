using System;
using System.Threading;
using System.Threading.Tasks;

namespace SecSMS.Common.Transport;

public enum TransportType
{
    Unknown = 0,
    Bluetooth = 1,
    Wifi = 2,
}

public enum TransportMessageType
{
    Unknown = 0,
    Test = 1,
    SmsPayload = 2,
    RsaPublicKey = 3,
    EncryptedOtp = 4,
}

public sealed class TransportEnvelope
{
    public TransportEnvelope(TransportMessageType type, byte[] payload)
    {
        Type = type;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
    }

    public TransportMessageType Type { get; }

    public byte[] Payload { get; }
}

public interface ITransportClient : IAsyncDisposable
{
    TransportType TransportType { get; }

    string Name { get; }

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task SendAsync(TransportEnvelope message, CancellationToken cancellationToken = default);

    event EventHandler<TransportEnvelope>? MessageReceived;

    event EventHandler<Exception>? Error;
}
