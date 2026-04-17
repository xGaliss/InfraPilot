namespace InfraPilot.Contracts.Actions;

public sealed record AgentActionResultReportDto(
    string InstallationId,
    string Status,
    string? ResultMessage,
    string? ErrorMessage,
    string? OutputJson,
    DateTimeOffset CompletedUtc);
