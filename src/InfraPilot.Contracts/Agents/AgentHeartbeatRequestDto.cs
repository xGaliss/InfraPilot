namespace InfraPilot.Contracts.Agents;

public sealed record AgentHeartbeatRequestDto(
    string InstallationId,
    string AgentVersion,
    DateTimeOffset SentUtc);
