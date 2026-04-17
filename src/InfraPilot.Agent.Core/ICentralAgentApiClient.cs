namespace InfraPilot.Agent.Core;

using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;

public interface ICentralAgentApiClient
{
    Task<AgentEnrollResponseDto> EnrollAsync(AgentIdentity identity, CancellationToken cancellationToken);

    Task SendHeartbeatAsync(AgentIdentity identity, CancellationToken cancellationToken);

    Task PublishCapabilitiesAsync(
        AgentIdentity identity,
        IReadOnlyList<CapabilityDescriptorDto> capabilities,
        CancellationToken cancellationToken);

    Task PublishSnapshotsAsync(
        AgentIdentity identity,
        IReadOnlyList<CapabilitySnapshotDto> snapshots,
        CancellationToken cancellationToken);

    Task<AgentActionCommandDto?> PullNextActionAsync(AgentIdentity identity, CancellationToken cancellationToken);

    Task ReportActionResultAsync(
        AgentIdentity identity,
        Guid actionId,
        AgentActionResultReportDto result,
        CancellationToken cancellationToken);
}
