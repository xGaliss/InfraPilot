namespace InfraPilot.Central.Application;

public sealed record StoredAgent(
    Guid AgentId,
    string InstallationId,
    string DisplayName,
    string MachineName,
    string Status,
    string AgentVersion,
    string AccessToken,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? ApprovedUtc,
    DateTimeOffset? LastSeenUtc);
