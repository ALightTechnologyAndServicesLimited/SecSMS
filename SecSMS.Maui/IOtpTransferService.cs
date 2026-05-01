using System.Threading;
using System.Threading.Tasks;

namespace SecSMS.Maui;

public interface IOtpTransferService
{
    Task SendAsync(string text, CancellationToken cancellationToken = default);
}
