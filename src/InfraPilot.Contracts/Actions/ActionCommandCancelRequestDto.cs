namespace InfraPilot.Contracts.Actions;

public sealed record ActionCommandCancelRequestDto(
    string RequestedBy,
    string? Reason);
