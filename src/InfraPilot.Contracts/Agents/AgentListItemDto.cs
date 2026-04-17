namespace InfraPilot.Contracts.Agents;

public sealed record AgentListItemDto(
    Guid AgentId,
    string InstallationId,
    string DisplayName,
    string MachineName,
    string Status,
    string AgentVersion,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ApprovedUtc,
    DateTimeOffset? LastSeenUtc,
    IReadOnlyList<string> CapabilityKeys,
    string? LastActionStatus,
    string? LastActionSummary);
