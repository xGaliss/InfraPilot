namespace InfraPilot.Capabilities.ScheduledTasks.Windows;

using InfraPilot.Capabilities.Abstractions;
using Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWindowsScheduledTasksCapability(this IServiceCollection services)
    {
        services.AddSingleton<ICapabilityModule, WindowsScheduledTasksCapabilityModule>();
        return services;
    }
}
