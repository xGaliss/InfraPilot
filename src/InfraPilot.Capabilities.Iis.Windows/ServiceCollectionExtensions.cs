namespace InfraPilot.Capabilities.Iis.Windows;

using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsIisCapability(this IServiceCollection services)
    {
        services.AddSingleton<ICapabilityModule, WindowsIisCapabilityModule>();
        return services;
    }
}
