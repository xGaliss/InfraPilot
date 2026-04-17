namespace InfraPilot.Contracts.Capabilities;

public sealed record CapabilitySnapshotDto(
    string CapabilityKey,
    string SchemaVersion,
    string Hash,
    string PayloadJson);
