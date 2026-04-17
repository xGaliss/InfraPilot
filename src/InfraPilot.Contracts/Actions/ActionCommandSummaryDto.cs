namespace InfraPilot.Contracts.Actions;

public sealed record ActionCommandSummaryDto(
    Guid ActionId,
    Guid AgentId,
    string CapabilityKey,
    string ActionKey,
    string? TargetKey,
    string Status,
    string RequestedBy,
    DateTimeOffset CreatedUtc,
    DateTimeOffset? LeasedUtc,
    DateTimeOffset? LeaseExpiresUtc,
    DateTimeOffset? CompletedUtc,
    int AttemptCount,
    string? ResultMessage,
    string? ErrorMessage);
