namespace InfraPilot.Contracts.Actions;

public sealed record AgentActionCommandDto(
    Guid ActionId,
    string CapabilityKey,
    string ActionKey,
    string? TargetKey,
    string? PayloadJson,
    string RequestedBy,
    DateTimeOffset CreatedUtc);
