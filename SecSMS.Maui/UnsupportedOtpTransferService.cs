using System;
using System.Threading;
using System.Threading.Tasks;

namespace SecSMS.Maui;

public sealed class UnsupportedOtpTransferService : IOtpTransferService
{
    public Task SendAsync(string text, CancellationToken cancellationToken = default)
    {
        throw new PlatformNotSupportedException("OTP transfer is only available on Android.");
    }
}
