namespace InfraPilot.Contracts.FileTree;

public sealed record FileTreeRootDto(
    string RootPath,
    IReadOnlyList<FileTreeNodeDto> Nodes);
