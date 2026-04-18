namespace InfraPilot.Contracts.Agents;

public sealed record AgentControlResultDto(
    Guid AgentId,
    string InstallationId,
    string Status,
    string Action,
    string Message,
    DateTimeOffset ProcessedUtc);
