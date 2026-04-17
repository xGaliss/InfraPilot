namespace InfraPilot.Contracts.Capabilities;

public sealed record AgentSnapshotPublishRequestDto(
    string InstallationId,
    DateTimeOffset CollectedUtc,
    IReadOnlyList<CapabilitySnapshotDto> Snapshots);
