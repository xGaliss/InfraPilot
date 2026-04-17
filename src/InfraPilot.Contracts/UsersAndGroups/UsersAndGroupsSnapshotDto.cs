namespace InfraPilot.Contracts.UsersAndGroups;

public sealed class UsersAndGroupsSnapshotDto
{
    public IReadOnlyList<LocalUserDto> Users { get; init; } = [];

    public IReadOnlyList<LocalGroupDto> Groups { get; init; } = [];

    public UsersAndGroupsSnapshotDto()
    {
    }

    public UsersAndGroupsSnapshotDto(IReadOnlyList<LocalUserDto> users, IReadOnlyList<LocalGroupDto> groups)
    {
        Users = users ?? [];
        Groups = groups ?? [];
    }
}
