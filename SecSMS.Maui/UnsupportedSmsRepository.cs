using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SecSMS.Common;

namespace SecSMS.Maui;

public sealed class UnsupportedSmsRepository : ISmsRepository
{
    const string Message = "SMS access is only available on Android.";

    public Task<IReadOnlyList<SmsMessageSummary>> GetRecentMessagesAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException(Message);
    }

    public Task<SmsMessageDetail?> GetMessageByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException(Message);
    }
}
