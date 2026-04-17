namespace InfraPilot.Contracts.Agents;

public sealed record AgentEnrollResponseDto(
    Guid AgentId,
    string Status,
    string AccessToken,
    string Message);
