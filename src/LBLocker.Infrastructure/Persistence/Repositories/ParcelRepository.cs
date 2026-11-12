using LBLocker.BusinessLogic.Interfaces;
using LBLocker.Domain.Parcels;
using Microsoft.EntityFrameworkCore;

namespace LBLocker.Infrastructure.Persistence.Repositories;

public sealed class ParcelRepository(LockerDbContext dbContext) : IParcelRepository
{
    public Task<Parcel?> GetByExternalParcelIdAsync(
        string externalParcelId,
        CancellationToken cancellationToken = default) =>
        dbContext.Parcels
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.ExternalParcelId == externalParcelId, cancellationToken);

    public async Task AddAsync(Parcel parcel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parcel);
        dbContext.Parcels.Add(parcel);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Parcel parcel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parcel);
        dbContext.Parcels.Update(parcel);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}