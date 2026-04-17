namespace InfraPilot.Contracts.FileTree;

public sealed class FileTreeRootDto
{
    public string RootPath { get; init; } = string.Empty;

    public IReadOnlyList<FileTreeNodeDto> Nodes { get; init; } = [];

    public FileTreeRootDto()
    {
    }

    public FileTreeRootDto(string rootPath, IReadOnlyList<FileTreeNodeDto> nodes)
    {
        RootPath = rootPath;
        Nodes = nodes ?? [];
    }
}
