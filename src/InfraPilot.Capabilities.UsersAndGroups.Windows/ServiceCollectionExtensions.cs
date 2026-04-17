namespace InfraPilot.Capabilities.UsersAndGroups.Windows;

using System.Runtime.Versioning;
using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[SupportedOSPlatform("windows")]
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsUsersAndGroupsCapability(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(UsersAndGroupsCapabilityOptions.SectionName).Get<UsersAndGroupsCapabilityOptions>()
            ?? new UsersAndGroupsCapabilityOptions();

        services.Configure<UsersAndGroupsCapabilityOptions>(configuration.GetSection(UsersAndGroupsCapabilityOptions.SectionName));

        if (!options.Enabled)
        {
            return services;
        }

        services.AddSingleton<ICapabilityModule, WindowsUsersAndGroupsCapabilityModule>();
        return services;
    }
}
