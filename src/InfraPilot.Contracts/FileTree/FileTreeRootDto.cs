namespace InfraPilot.Contracts.FileTree;

public sealed class FileTreeRootDto
{
    public string RootPath { get; init; } = string.Empty;

    public string? Owner { get; init; }

    public IReadOnlyList<string> Permissions { get; init; } = [];

    public IReadOnlyList<FileTreeNodeDto> Nodes { get; init; } = [];

    public FileTreeRootDto()
    {
    }

    public FileTreeRootDto(string rootPath, string? owner, IReadOnlyList<string> permissions, IReadOnlyList<FileTreeNodeDto> nodes)
    {
        RootPath = rootPath;
        Owner = owner;
        Permissions = permissions ?? [];
        Nodes = nodes ?? [];
    }
}
