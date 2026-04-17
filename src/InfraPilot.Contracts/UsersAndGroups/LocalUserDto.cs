namespace InfraPilot.Contracts.UsersAndGroups;

public sealed class LocalUserDto
{
    public string Name { get; init; } = string.Empty;

    public string? FullName { get; init; }

    public string? Description { get; init; }

    public string? Sid { get; init; }

    public bool Enabled { get; init; }

    public bool PasswordRequired { get; init; }

    public bool PasswordExpires { get; init; }

    public string? LastLogonRaw { get; init; }
}
