namespace InfraPilot.Contracts.Changes;

public sealed record CapabilityChangeEventDto(
    Guid ChangeEventId,
    Guid AgentId,
    string CapabilityKey,
    string ChangeKind,
    DateTimeOffset CollectedUtc,
    DateTimeOffset? PreviousCollectedUtc,
    string Summary,
    IReadOnlyList<string> Highlights);
