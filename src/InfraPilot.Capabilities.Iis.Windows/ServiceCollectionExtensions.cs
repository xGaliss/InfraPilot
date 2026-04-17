namespace InfraPilot.Capabilities.Iis.Windows;

using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsIisCapability(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(IisCapabilityOptions.SectionName).Get<IisCapabilityOptions>()
            ?? new IisCapabilityOptions();

        services.Configure<IisCapabilityOptions>(configuration.GetSection(IisCapabilityOptions.SectionName));

        if (!options.Enabled)
        {
            return services;
        }

        services.AddSingleton<ICapabilityModule, WindowsIisCapabilityModule>();
        return services;
    }
}
