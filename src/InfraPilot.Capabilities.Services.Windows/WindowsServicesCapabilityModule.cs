namespace InfraPilot.Capabilities.Services.Windows;

using InfraPilot.Capabilities.Abstractions;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Services;
using Microsoft.Extensions.Options;
using System.ServiceProcess;

public sealed class WindowsServicesCapabilityModule : ICapabilityModule
{
    private readonly ServicesCapabilityOptions _options;

    private static readonly CapabilityDescriptorDto Descriptor = new(
        CapabilityKeys.Services,
        "Windows Services",
        "1.0.0",
        [
            new CapabilityActionDefinitionDto("start", "Start service", true, "Starts a stopped service."),
            new CapabilityActionDefinitionDto("stop", "Stop service", true, "Stops a running service."),
            new CapabilityActionDefinitionDto("restart", "Restart service", true, "Restarts a running service.")
        ]);

    public WindowsServicesCapabilityModule(IOptions<ServicesCapabilityOptions> options)
    {
        _options = options.Value;
    }

    public CapabilityDescriptorDto Describe() => Descriptor;

    public Task<CapabilitySnapshotResult> CollectSnapshotAsync(CancellationToken cancellationToken)
    {
        var services = ServiceController.GetServices()
            .Where(service => CapabilityFilter.Matches(
                $"{service.ServiceName} {service.DisplayName}",
                _options.IncludeNames,
                _options.ExcludeNames))
            .OrderBy(service => service.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(service => new ServiceStatusDto(
                service.ServiceName,
                service.DisplayName,
                service.Status.ToString(),
                service.CanStop.ToString(),
                service.ServiceType.ToString()))
            .ToList();

        var payload = new ServiceSnapshotDto(services);
        return Task.FromResult(new CapabilitySnapshotResult(CapabilityKeys.Services, "1.0.0", payload));
    }

    public async Task<CapabilityActionExecutionResult> ExecuteActionAsync(
        AgentActionCommandDto command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.TargetKey))
        {
            return new CapabilityActionExecutionResult(false, "A target service name is required.", "Missing target.");
        }

        using var controller = new ServiceController(command.TargetKey);

        try
        {
            _ = controller.Status;
        }
        catch (InvalidOperationException ex)
        {
            return new CapabilityActionExecutionResult(false, ex.Message, ex.ToString());
        }

        return command.ActionKey switch
        {
            "start" => await StartAsync(controller, cancellationToken),
            "stop" => await StopAsync(controller, cancellationToken),
            "restart" => await RestartAsync(controller, cancellationToken),
            _ => new CapabilityActionExecutionResult(false, $"Unsupported action '{command.ActionKey}'.", "Unsupported action.")
        };
    }

    private static Task<CapabilityActionExecutionResult> StartAsync(ServiceController controller, CancellationToken cancellationToken)
    {
        if (controller.Status == ServiceControllerStatus.Running)
        {
            return Task.FromResult(new CapabilityActionExecutionResult(true, $"Service '{controller.ServiceName}' is already running."));
        }

        controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new CapabilityActionExecutionResult(true, $"Service '{controller.ServiceName}' started successfully."));
    }

    private static Task<CapabilityActionExecutionResult> StopAsync(ServiceController controller, CancellationToken cancellationToken)
    {
        if (!controller.CanStop)
        {
            return Task.FromResult(new CapabilityActionExecutionResult(false, $"Service '{controller.ServiceName}' cannot be stopped.", "Stop not allowed."));
        }

        if (controller.Status == ServiceControllerStatus.Stopped)
        {
            return Task.FromResult(new CapabilityActionExecutionResult(true, $"Service '{controller.ServiceName}' is already stopped."));
        }

        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new CapabilityActionExecutionResult(true, $"Service '{controller.ServiceName}' stopped successfully."));
    }

    private static async Task<CapabilityActionExecutionResult> RestartAsync(ServiceController controller, CancellationToken cancellationToken)
    {
        if (controller.Status != ServiceControllerStatus.Stopped)
        {
            var stopResult = await StopAsync(controller, cancellationToken);
            if (!stopResult.Succeeded)
            {
                return stopResult;
            }
        }

        controller.Refresh();
        return await StartAsync(controller, cancellationToken);
    }
}
