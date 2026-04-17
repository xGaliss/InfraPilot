namespace InfraPilot.Contracts.Agents;

public sealed record AgentListItemDto(
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
    IReadOnlyList<string> CapabilityKeys,
    string? LastActionStatus,
    string? LastActionSummary,
    int PendingActionCount,
    int InProgressActionCount,
    int FailedActionCount);
