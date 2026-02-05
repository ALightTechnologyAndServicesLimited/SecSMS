using System;

namespace SecSMS.Common;

public sealed class SmsMessageDetail
{
    public SmsMessageDetail(string id, string fromNumber, SimSlot simSlot, DateTimeOffset receivedAt, string body)
    {
        Id = id;
        FromNumber = fromNumber;
        SimSlot = simSlot;
        ReceivedAt = receivedAt;
        Body = body;
    }

    public string Id { get; }

    public string FromNumber { get; }

    public SimSlot SimSlot { get; }

    public DateTimeOffset ReceivedAt { get; }

    public string Body { get; }
}
