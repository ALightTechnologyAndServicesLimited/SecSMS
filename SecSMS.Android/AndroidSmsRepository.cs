using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Android.Database;
using Android.Provider;
using SecSMS.Common;

namespace SecSMS.Android;

public sealed class AndroidSmsRepository : ISmsRepository
{
    readonly ContentResolver _contentResolver;

    public AndroidSmsRepository(ContentResolver contentResolver)
    {
        _contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
    }

    public Task<IReadOnlyList<SmsMessageSummary>> GetRecentMessagesAsync(TimeSpan window, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var result = new List<SmsMessageSummary>();

            var threshold = DateTimeOffset.UtcNow.Subtract(window).ToUnixTimeMilliseconds();
            var selection = "date >= ?";
            var selectionArgs = new[] { threshold.ToString(CultureInfo.InvariantCulture) };
            var projection = new[] { "_id", "address", "date", "sub_id" };

            ICursor? cursor = null;

            try
            {
                cursor = _contentResolver.Query(Telephony.Sms.Inbox.ContentUri, projection, selection, selectionArgs, "date DESC");

                if (cursor == null)
                {
                    return (IReadOnlyList<SmsMessageSummary>)result;
                }

                var idIndex = cursor.GetColumnIndex("_id");
                var addressIndex = cursor.GetColumnIndex("address");
                var dateIndex = cursor.GetColumnIndex("date");
                var subIdIndex = cursor.GetColumnIndex("sub_id");

                while (cursor.MoveToNext())
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    if (idIndex < 0 || addressIndex < 0 || dateIndex < 0)
                    {
                        continue;
                    }

                    var id = cursor.GetString(idIndex) ?? string.Empty;
                    var address = cursor.GetString(addressIndex) ?? string.Empty;
                    var dateMillis = cursor.GetLong(dateIndex);
                    var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(dateMillis);

                    var simSlot = SimSlot.Unknown;
                    if (subIdIndex >= 0)
                    {
                        var subId = cursor.GetInt(subIdIndex);
                        simSlot = subId switch
                        {
                            1 => SimSlot.Sim1,
                            2 => SimSlot.Sim2,
                            _ => SimSlot.Unknown
                        };
                    }

                    result.Add(new SmsMessageSummary(id, address, simSlot, receivedAt));
                }
            }
            finally
            {
                cursor?.Close();
            }

            return (IReadOnlyList<SmsMessageSummary>)result;
        }, cancellationToken);
    }

    public Task<SmsMessageDetail?> GetMessageByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new ArgumentException("Value cannot be null or empty.", nameof(id));
        }

        return Task.Run(() =>
        {
            ICursor? cursor = null;

            try
            {
                var uri = Telephony.Sms.Inbox.ContentUri;
                var selection = "_id = ?";
                var selectionArgs = new[] { id };
                var projection = new[] { "_id", "address", "date", "body", "sub_id" };

                cursor = _contentResolver.Query(uri, projection, selection, selectionArgs, null);

                if (cursor == null || !cursor.MoveToFirst())
                {
                    return null;
                }

                var idIndex = cursor.GetColumnIndex("_id");
                var addressIndex = cursor.GetColumnIndex("address");
                var dateIndex = cursor.GetColumnIndex("date");
                var bodyIndex = cursor.GetColumnIndex("body");
                var subIdIndex = cursor.GetColumnIndex("sub_id");

                if (idIndex < 0 || addressIndex < 0 || dateIndex < 0 || bodyIndex < 0)
                {
                    return null;
                }

                var messageId = cursor.GetString(idIndex) ?? string.Empty;
                var address = cursor.GetString(addressIndex) ?? string.Empty;
                var dateMillis = cursor.GetLong(dateIndex);
                var body = cursor.GetString(bodyIndex) ?? string.Empty;

                var receivedAt = DateTimeOffset.FromUnixTimeMilliseconds(dateMillis);

                var simSlot = SimSlot.Unknown;
                if (subIdIndex >= 0)
                {
                    var subId = cursor.GetInt(subIdIndex);
                    simSlot = subId switch
                    {
                        1 => SimSlot.Sim1,
                        2 => SimSlot.Sim2,
                        _ => SimSlot.Unknown
                    };
                }

                return new SmsMessageDetail(messageId, address, simSlot, receivedAt, body);
            }
            finally
            {
                cursor?.Close();
            }
        }, cancellationToken);
    }
}
