using Droppa.Models;

namespace Droppa.Services;

/// <summary>Provides the catalogue of partnered courier services (loaded from the API).</summary>
public interface ICourierRepository
{
    Task<IReadOnlyList<CourierService>> GetAllAsync(CancellationToken ct = default);
}
