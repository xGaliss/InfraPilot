namespace InfraPilot.Central.Application;

using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Changes;

public interface ICentralStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<StoredAgent?> GetAgentByInstallationIdAsync(string installationId, CancellationToken cancellationToken);

    Task UpsertAgentAsync(StoredAgent agent, CancellationToken cancellationToken);

    Task TouchHeartbeatAsync(Guid agentId, string agentVersion, DateTimeOffset lastSeenUtc, CancellationToken cancellationToken);

    Task ReplaceCapabilitiesAsync(Guid agentId, IReadOnlyList<CapabilityDescriptorDto> capabilities, CancellationToken cancellationToken);

    Task AddSnapshotsAsync(Guid agentId, DateTimeOffset collectedUtc, IReadOnlyList<CapabilitySnapshotDto> snapshots, CancellationToken cancellationToken);

    Task<AgentActionCommandDto?> LeaseNextActionAsync(Guid agentId, TimeSpan leaseDuration, CancellationToken cancellationToken);

    Task<bool> CompleteActionAsync(Guid agentId, Guid actionId, AgentActionResultReportDto result, CancellationToken cancellationToken);

    Task<bool> HasQueuedActionAsync(Guid agentId, string capabilityKey, string actionKey, string? targetKey, CancellationToken cancellationToken);

    Task<IReadOnlyList<AgentListItemDto>> GetAgentsAsync(CancellationToken cancellationToken);

    Task<AgentDetailDto?> GetAgentDetailAsync(Guid agentId, CancellationToken cancellationToken);

    Task<bool> ApproveAgentAsync(Guid agentId, CancellationToken cancellationToken);

    Task<ActionCommandSummaryDto> CreateActionAsync(ActionCommandCreateRequestDto request, CancellationToken cancellationToken);

    Task<IReadOnlyList<CapabilitySnapshotHistoryItemDto>> GetCapabilityHistoryAsync(
        Guid agentId,
        string capabilityKey,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CapabilityChangeEventDto>> GetChangeFeedAsync(
        Guid? agentId,
        int take,
        CancellationToken cancellationToken);

    Task<ActionCommandSummaryDto?> CancelPendingActionAsync(
        Guid actionId,
        string requestedBy,
        string? reason,
        CancellationToken cancellationToken);
}
