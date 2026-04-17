namespace InfraPilot.Contracts.Capabilities;

public sealed record AgentCapabilityStateDto(
    CapabilityDescriptorDto Descriptor,
    DateTimeOffset? LastCollectedUtc,
    string? LatestPayloadJson,
    string? LatestHash,
    CapabilityChangeSummaryDto ChangeSummary);
