using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SecSMS.Common.Totp;

public interface ITotpRepository
{
    Task<IReadOnlyList<TotpEntry>> ListAsync(CancellationToken cancellationToken = default);

    Task<TotpEntry> AddAsync(TotpEntry entry, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
