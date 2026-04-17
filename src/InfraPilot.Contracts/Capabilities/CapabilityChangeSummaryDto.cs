namespace InfraPilot.Contracts.Capabilities;

public sealed record CapabilityChangeSummaryDto(
    bool HasPreviousSnapshot,
    bool HasChanges,
    DateTimeOffset? PreviousCollectedUtc,
    string Summary,
    IReadOnlyList<string> Highlights);
