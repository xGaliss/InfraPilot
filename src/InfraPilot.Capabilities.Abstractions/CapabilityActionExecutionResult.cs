namespace InfraPilot.Capabilities.Abstractions;

public sealed record CapabilityActionExecutionResult(
    bool Succeeded,
    string ResultMessage,
    string? ErrorMessage = null,
    string? OutputJson = null);
