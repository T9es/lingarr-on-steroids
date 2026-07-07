using Lingarr.Server.Models.CustomSources;

namespace Lingarr.Server.Interfaces.Services;

public interface ICustomSourceScannerService
{
    Task<CustomSourceScanResult> ScanSourceAsync(int customSourceId, CancellationToken cancellationToken = default);
}
