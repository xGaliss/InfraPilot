namespace InfraPilot.Capabilities.Services.Windows;

using System.Runtime.Versioning;
using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[SupportedOSPlatform("windows")]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsServicesCapability(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ServicesCapabilityOptions.SectionName).Get<ServicesCapabilityOptions>()
            ?? new ServicesCapabilityOptions();

        services.Configure<ServicesCapabilityOptions>(configuration.GetSection(ServicesCapabilityOptions.SectionName));

        if (!options.Enabled)
        {
            return services;
        }

        services.AddSingleton<ICapabilityModule, WindowsServicesCapabilityModule>();
        return services;
    }
}
