namespace InfraPilot.Capabilities.Abstractions;

public sealed record CapabilitySnapshotResult(
    string CapabilityKey,
    string SchemaVersion,
    object Payload);
