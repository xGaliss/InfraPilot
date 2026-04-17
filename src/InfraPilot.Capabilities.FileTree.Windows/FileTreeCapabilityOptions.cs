namespace InfraPilot.Capabilities.FileTree.Windows;

public sealed class FileTreeCapabilityOptions
{
    public const string SectionName = "Capabilities:FileTree";

    public bool Enabled { get; set; } = true;

    public int MaxDepth { get; set; } = 2;

    public bool IncludePermissions { get; set; } = true;

    public int MaxPermissionEntries { get; set; } = 6;

    public List<string> Roots { get; set; } = [];
}
