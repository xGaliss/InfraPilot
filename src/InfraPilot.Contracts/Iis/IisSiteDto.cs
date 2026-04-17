namespace InfraPilot.Contracts.Iis;

public sealed class IisSiteDto
{
    public string Name { get; init; } = string.Empty;

    public string State { get; init; } = string.Empty;

    public IReadOnlyList<string> Bindings { get; init; } = [];

    public IisSiteDto()
    {
    }

    public IisSiteDto(string name, string state, IReadOnlyList<string> bindings)
    {
        Name = name;
        State = state;
        Bindings = bindings ?? [];
    }
}
