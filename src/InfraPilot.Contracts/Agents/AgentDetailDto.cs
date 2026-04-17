namespace InfraPilot.Contracts.Agents;

using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Changes;

public sealed record AgentDetailDto(
    Guid AgentId,
    string InstallationId,
    string DisplayName,
    string MachineName,
    string Status,
    string HealthStatus,
    string AgentVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ApprovedUtc,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset? LastCollectedUtc,
    int PendingActionCount,
    int InProgressActionCount,
    int FailedActionCount,
    IReadOnlyList<AgentCapabilityStateDto> Capabilities,
    IReadOnlyList<ActionCommandSummaryDto> RecentActions,
    IReadOnlyList<CapabilityChangeEventDto> RecentChanges);
