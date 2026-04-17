namespace InfraPilot.Contracts.Actions;

public sealed record ActionCommandCreateRequestDto(
    Guid AgentId,
    string CapabilityKey,
    string ActionKey,
    string? TargetKey,
    string? PayloadJson,
    string RequestedBy);
