using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SecSMS.Common;

public interface ISmsRepository
{
    Task<IReadOnlyList<SmsMessageSummary>> GetRecentMessagesAsync(TimeSpan window, CancellationToken cancellationToken = default);

    Task<SmsMessageDetail?> GetMessageByIdAsync(string id, CancellationToken cancellationToken = default);
}
