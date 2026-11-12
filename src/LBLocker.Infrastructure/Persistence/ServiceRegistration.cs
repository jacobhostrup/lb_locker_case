using LBLocker.BusinessLogic.Interfaces;
using LBLocker.Infrastructure.FileImport;
using LBLocker.Infrastructure.Http;
using LBLocker.Infrastructure.Persistence.Repositories;
using LBLocker.Infrastructure.Security;
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
        services.AddSingleton<PinGenerator>();
        services.AddSingleton<PinCredentialProtector>();
        services.AddScoped<IParcelFileReader, CsvParcelFileReader>();
        services.AddHttpClient<ILockerApiClient, LockerApiClient>(client =>
            client.BaseAddress = new Uri("http://localhost:18080/api/v1/"));
        services.AddHttpClient<INotificationApiClient, NotificationApiClient>(client =>
            client.BaseAddress = new Uri("http://localhost:18080/api/v1/"));
        return services;
    }
}
