namespace InfraPilot.Contracts.Capabilities;

public sealed record CapabilitySnapshotHistoryItemDto(
    Guid SnapshotId,
    string CapabilityKey,
    DateTimeOffset CollectedUtc,
    string Hash,
    string PayloadJson,
    CapabilityChangeSummaryDto ChangeSummary);
