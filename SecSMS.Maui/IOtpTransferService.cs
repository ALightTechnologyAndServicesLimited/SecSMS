using System.Threading;
using System.Threading.Tasks;

namespace SecSMS.Maui;

public interface IOtpTransferService
{
    Task SendAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Connects to the desktop, sends this device's public PQ key bundle, and waits
    /// for an encrypted TOTP enrollment (otpauth:// URI or bare Base32 secret).
    /// Returns the decrypted payload; it is never displayed or logged by the service.
    /// </summary>
    Task<string> ReceiveTotpEnrollmentAsync(CancellationToken cancellationToken = default);
}
