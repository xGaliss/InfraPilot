namespace InfraPilot.Contracts.Capabilities;

public sealed record CapabilityActionDefinitionDto(
    string ActionKey,
    string DisplayName,
    bool RequiresTarget,
    string? Description = null);
