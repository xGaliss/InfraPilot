namespace InfraPilot.Contracts.FileTree;

public sealed class FileTreeSnapshotDto
{
    public IReadOnlyList<FileTreeRootDto> Roots { get; init; } = [];

    public FileTreeSnapshotDto()
    {
    }

    public FileTreeSnapshotDto(IReadOnlyList<FileTreeRootDto> roots)
    {
        Roots = roots ?? [];
    }
}
