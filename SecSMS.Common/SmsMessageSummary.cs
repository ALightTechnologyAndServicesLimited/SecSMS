using System;

namespace SecSMS.Common;

public sealed class SmsMessageSummary
{
    public SmsMessageSummary(string id, string fromNumber, SimSlot simSlot, DateTimeOffset receivedAt)
    {
        Id = id;
        FromNumber = fromNumber;
        SimSlot = simSlot;
        ReceivedAt = receivedAt;
    }

    public string Id { get; }

    public string FromNumber { get; }

    public SimSlot SimSlot { get; }

    public DateTimeOffset ReceivedAt { get; }
}
