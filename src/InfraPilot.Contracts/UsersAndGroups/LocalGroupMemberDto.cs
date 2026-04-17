namespace InfraPilot.Contracts.UsersAndGroups;

public sealed class LocalGroupMemberDto
{
    public string Name { get; init; } = string.Empty;

    public string? ObjectClass { get; init; }

    public string? PrincipalSource { get; init; }

    public string? Sid { get; init; }
}
