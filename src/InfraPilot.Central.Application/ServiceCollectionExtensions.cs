namespace InfraPilot.Central.Application;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfraPilotCentralApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<CentralOptions>(configuration.GetSection(CentralOptions.SectionName));
        services.AddScoped<CentralService>();
        return services;
    }
}
