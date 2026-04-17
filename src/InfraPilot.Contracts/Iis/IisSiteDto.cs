namespace InfraPilot.Contracts.Iis;

public sealed record IisSiteDto(
    string Name,
    string State,
    IReadOnlyList<string> Bindings);
