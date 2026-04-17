namespace InfraPilot.Capabilities.FileTree.Windows;

using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using InfraPilot.Capabilities.Abstractions;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.FileTree;
using Microsoft.Extensions.Options;

[SupportedOSPlatform("windows")]
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
            .Select(path =>
            {
                var rootInfo = new DirectoryInfo(path);
                return new FileTreeRootDto(
                    path,
                    TryGetOwner(rootInfo),
                    TryGetPermissions(rootInfo),
                    BuildNodes(path, depth: 0, cancellationToken));
            })
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
                    Owner = TryGetOwner(info),
                    Permissions = TryGetPermissions(info),
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
                    SizeBytes = info.Exists ? info.Length : null,
                    Owner = TryGetOwner(info),
                    Permissions = TryGetPermissions(info)
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

    private string? TryGetOwner(FileSystemInfo info)
    {
        if (!_options.IncludePermissions)
        {
            return null;
        }

        try
        {
            var security = GetSecurity(info);
            var owner = security?.GetOwner(typeof(NTAccount));
            return owner?.Value;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<string> TryGetPermissions(FileSystemInfo info)
    {
        if (!_options.IncludePermissions)
        {
            return [];
        }

        try
        {
            var security = GetSecurity(info);
            if (security is null)
            {
                return [];
            }

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(NTAccount));
            var entries = new List<string>();

            foreach (FileSystemAccessRule rule in rules)
            {
                var identity = rule.IdentityReference?.Value;
                if (string.IsNullOrWhiteSpace(identity))
                {
                    continue;
                }

                var accessType = rule.AccessControlType == AccessControlType.Allow ? "Allow" : "Deny";
                var inheritance = rule.IsInherited ? "Inherited" : "Explicit";
                entries.Add($"{identity}: {accessType} {rule.FileSystemRights} ({inheritance})");

                if (entries.Count >= Math.Max(1, _options.MaxPermissionEntries))
                {
                    break;
                }
            }

            return entries;
        }
        catch
        {
            return [];
        }
    }

    private static FileSystemSecurity? GetSecurity(FileSystemInfo info)
    {
        return info switch
        {
            DirectoryInfo directory => directory.GetAccessControl(),
            FileInfo file => file.GetAccessControl(),
            _ => null
        };
    }
}
