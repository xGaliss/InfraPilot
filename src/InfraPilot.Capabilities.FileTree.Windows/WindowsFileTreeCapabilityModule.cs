namespace InfraPilot.Capabilities.FileTree.Windows;

using InfraPilot.Capabilities.Abstractions;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.FileTree;
using Microsoft.Extensions.Options;

public sealed class WindowsFileTreeCapabilityModule : ICapabilityModule
{
    private readonly FileTreeCapabilityOptions _options;

    private static readonly CapabilityDescriptorDto Descriptor = new(
        CapabilityKeys.FileTree,
        "File Tree",
        "1.0.0",
        []);

    public WindowsFileTreeCapabilityModule(IOptions<FileTreeCapabilityOptions> options)
    {
        _options = options.Value;
    }

    public CapabilityDescriptorDto Describe() => Descriptor;

    public Task<CapabilitySnapshotResult> CollectSnapshotAsync(CancellationToken cancellationToken)
    {
        var roots = _options.Roots
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .Select(path => new FileTreeRootDto(path, BuildNodes(path, depth: 0, cancellationToken)))
            .ToList();

        return Task.FromResult(new CapabilitySnapshotResult(
            CapabilityKeys.FileTree,
            "1.0.0",
            new FileTreeSnapshotDto(roots)));
    }

    public Task<CapabilityActionExecutionResult> ExecuteActionAsync(
        AgentActionCommandDto command,
        CancellationToken cancellationToken)
        => Task.FromResult(new CapabilityActionExecutionResult(false, "The fileTree capability is read-only in the MVP.", "No supported actions."));

    private IReadOnlyList<FileTreeNodeDto> BuildNodes(string currentPath, int depth, CancellationToken cancellationToken)
    {
        if (depth >= Math.Max(1, _options.MaxDepth))
        {
            return [];
        }

        var nodes = new List<FileTreeNodeDto>();

        try
        {
            foreach (var directory in Directory.GetDirectories(currentPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new DirectoryInfo(directory);
                nodes.Add(new FileTreeNodeDto
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = true,
                    Children = BuildNodes(info.FullName, depth + 1, cancellationToken)
                });
            }

            foreach (var file in Directory.GetFiles(currentPath))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(file);
                nodes.Add(new FileTreeNodeDto
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    IsDirectory = false,
                    SizeBytes = info.Exists ? info.Length : null
                });
            }
        }
        catch
        {
            return nodes;
        }

        return nodes
            .OrderByDescending(node => node.IsDirectory)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
