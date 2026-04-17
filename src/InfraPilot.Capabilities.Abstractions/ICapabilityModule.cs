namespace InfraPilot.Capabilities.Abstractions;

using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;

public interface ICapabilityModule
{
    CapabilityDescriptorDto Describe();

    Task<CapabilitySnapshotResult> CollectSnapshotAsync(CancellationToken cancellationToken);

    Task<CapabilityActionExecutionResult> ExecuteActionAsync(
        AgentActionCommandDto command,
        CancellationToken cancellationToken);
}
