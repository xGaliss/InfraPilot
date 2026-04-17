namespace InfraPilot.Contracts.UsersAndGroups;

public sealed class LocalGroupDto
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? Sid { get; init; }

    public IReadOnlyList<LocalGroupMemberDto> Members { get; init; } = [];
}
