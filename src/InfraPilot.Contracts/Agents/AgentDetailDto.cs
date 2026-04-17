namespace InfraPilot.Contracts.Agents;

using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;

public sealed record AgentDetailDto(
    Guid AgentId,
    string InstallationId,
    string DisplayName,
    string MachineName,
    string Status,
    string AgentVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ApprovedUtc,
    DateTimeOffset? LastSeenUtc,
    IReadOnlyList<AgentCapabilityStateDto> Capabilities,
    IReadOnlyList<ActionCommandSummaryDto> RecentActions);
