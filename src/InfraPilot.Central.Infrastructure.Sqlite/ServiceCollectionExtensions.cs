namespace InfraPilot.Central.Infrastructure.Sqlite;

using InfraPilot.Central.Application;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfraPilotSqliteStore(this IServiceCollection services)
    {
        services.AddSingleton<ICentralStore, SqliteCentralStore>();
        return services;
    }
}
