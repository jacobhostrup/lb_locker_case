using LBLocker.BusinessLogic.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace LBLocker.BusinessLogic.Services;

public static class ServiceRegistration
{
    public static IServiceCollection AddBusinessLogic(this IServiceCollection services)
    {
        services.AddScoped<ParcelImportService>();
        return services;
    }
}
