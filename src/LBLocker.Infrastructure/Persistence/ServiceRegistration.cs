using LBLocker.BusinessLogic.Interfaces;
using LBLocker.Infrastructure.FileImport;
using LBLocker.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LBLocker.Infrastructure.Persistence;

public static class ServiceRegistration
{
    public static IServiceCollection AddLockerPersistence(
        this IServiceCollection services,
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        services.AddDbContext<LockerDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath}"));

        services.AddScoped<IParcelRepository, ParcelRepository>();
        services.AddScoped<IParcelFileReader, CsvParcelFileReader>();
        return services;
    }
}
