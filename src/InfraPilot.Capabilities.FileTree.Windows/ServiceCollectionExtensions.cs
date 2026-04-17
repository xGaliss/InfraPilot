namespace InfraPilot.Capabilities.FileTree.Windows;

using System.Runtime.Versioning;
using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[SupportedOSPlatform("windows")]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsFileTreeCapability(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(FileTreeCapabilityOptions.SectionName).Get<FileTreeCapabilityOptions>()
            ?? new FileTreeCapabilityOptions();

        services.Configure<FileTreeCapabilityOptions>(configuration.GetSection(FileTreeCapabilityOptions.SectionName));

        if (!options.Enabled)
        {
            return services;
        }

        services.AddSingleton<ICapabilityModule, WindowsFileTreeCapabilityModule>();
        return services;
    }
}
