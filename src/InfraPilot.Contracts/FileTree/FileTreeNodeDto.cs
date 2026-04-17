namespace InfraPilot.Contracts.FileTree;

public sealed class FileTreeNodeDto
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
    public IReadOnlyList<FileTreeNodeDto> Children { get; init; } = [];
}
