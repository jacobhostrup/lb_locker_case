using LBLocker.BusinessLogic.Contracts;

namespace LBLocker.BusinessLogic.Interfaces;

public interface IParcelFileReader
{
    Task<IReadOnlyCollection<ParcelImportRecord>> ReadAsync(
        Stream input,
        CancellationToken cancellationToken = default);
}
