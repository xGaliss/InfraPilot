namespace InfraPilot.Capabilities.Services.Windows;

using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsServicesCapability(this IServiceCollection services)
    {
        services.AddSingleton<ICapabilityModule, WindowsServicesCapabilityModule>();
        return services;
    }
}
