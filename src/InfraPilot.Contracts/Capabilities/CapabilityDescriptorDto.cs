namespace InfraPilot.Contracts.Capabilities;

public sealed record CapabilityDescriptorDto(
    string CapabilityKey,
    string DisplayName,
    string Version,
    IReadOnlyList<CapabilityActionDefinitionDto> Actions);
