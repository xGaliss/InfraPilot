namespace InfraPilot.Contracts.FileTree;

public sealed class FileTreeNodeDto
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
    public string? Owner { get; init; }
    public IReadOnlyList<string> Permissions { get; init; } = [];
    public IReadOnlyList<FileTreeNodeDto> Children { get; init; } = [];
}
