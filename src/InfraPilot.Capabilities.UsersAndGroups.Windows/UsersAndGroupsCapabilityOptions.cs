namespace InfraPilot.Capabilities.UsersAndGroups.Windows;

public sealed class UsersAndGroupsCapabilityOptions
{
    public const string SectionName = "Capabilities:UsersAndGroups";

    public bool Enabled { get; set; } = true;
}
