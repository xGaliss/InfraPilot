namespace InfraPilot.Contracts.Agents;

public sealed record AgentEnrollRequestDto(
    string InstallationId,
    string DisplayName,
    string MachineName,
    string AgentVersion);
