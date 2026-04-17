namespace InfraPilot.Contracts.Iis;

public sealed record IisAppPoolDto(
    string Name,
    string State);
