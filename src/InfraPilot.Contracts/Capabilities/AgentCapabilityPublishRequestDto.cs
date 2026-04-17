namespace InfraPilot.Contracts.Capabilities;

public sealed record AgentCapabilityPublishRequestDto(
    string InstallationId,
    IReadOnlyList<CapabilityDescriptorDto> Capabilities);
