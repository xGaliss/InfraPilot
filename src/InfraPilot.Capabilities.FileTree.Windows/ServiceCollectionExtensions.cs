namespace InfraPilot.Capabilities.FileTree.Windows;

using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsFileTreeCapability(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileTreeCapabilityOptions>(configuration.GetSection(FileTreeCapabilityOptions.SectionName));
        services.AddSingleton<ICapabilityModule, WindowsFileTreeCapabilityModule>();
        return services;
    }
}
