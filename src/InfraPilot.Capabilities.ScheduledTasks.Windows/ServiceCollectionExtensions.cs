namespace InfraPilot.Capabilities.ScheduledTasks.Windows;

using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsScheduledTasksCapability(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(ScheduledTasksCapabilityOptions.SectionName).Get<ScheduledTasksCapabilityOptions>()
            ?? new ScheduledTasksCapabilityOptions();

        services.Configure<ScheduledTasksCapabilityOptions>(configuration.GetSection(ScheduledTasksCapabilityOptions.SectionName));

        if (!options.Enabled)
        {
            return services;
        }

        services.AddSingleton<ICapabilityModule, WindowsScheduledTasksCapabilityModule>();
        return services;
    }
}
