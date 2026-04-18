namespace InfraPilot.Contracts.Agents;

public sealed record AgentControlRequestDto(
    string RequestedBy,
    string? Reason);
