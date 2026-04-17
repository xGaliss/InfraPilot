namespace InfraPilot.Capabilities.UsersAndGroups.Windows;

using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using InfraPilot.Capabilities.Abstractions;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.UsersAndGroups;

[SupportedOSPlatform("windows")]
public sealed class WindowsUsersAndGroupsCapabilityModule : ICapabilityModule
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly CapabilityDescriptorDto Descriptor = new(
        CapabilityKeys.UsersAndGroups,
        "Users & Groups",
        "1.0.0",
        []);

    public CapabilityDescriptorDto Describe() => Descriptor;

    public async Task<CapabilitySnapshotResult> CollectSnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = await CollectSnapshotInternalAsync(cancellationToken);
        return new CapabilitySnapshotResult(CapabilityKeys.UsersAndGroups, "1.0.0", snapshot);
    }

    public Task<CapabilityActionExecutionResult> ExecuteActionAsync(
        AgentActionCommandDto command,
        CancellationToken cancellationToken)
        => Task.FromResult(new CapabilityActionExecutionResult(false, "The usersAndGroups capability is read-only in the MVP.", "No supported actions."));

    private static async Task<UsersAndGroupsSnapshotDto> CollectSnapshotInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {BuildEncodedCommand(BuildSnapshotScript())}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return new UsersAndGroupsSnapshotDto();
            }

            return JsonSerializer.Deserialize<UsersAndGroupsSnapshotDto>(output, SnapshotJsonOptions)
                ?? new UsersAndGroupsSnapshotDto();
        }
        catch
        {
            return new UsersAndGroupsSnapshotDto();
        }
    }

    private static string BuildEncodedCommand(string script)
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static string BuildSnapshotScript()
        => """
$users = @(
    Get-LocalUser |
    Sort-Object Name |
    ForEach-Object {
        [pscustomobject]@{
            name = $_.Name
            fullName = $_.FullName
            description = $_.Description
            sid = if ($_.SID) { $_.SID.Value } else { $null }
            enabled = [bool]$_.Enabled
            passwordRequired = [bool]$_.PasswordRequired
            passwordExpires = [bool]$_.PasswordExpires
            lastLogonRaw = if ($_.LastLogon) { $_.LastLogon.ToString("o") } else { $null }
        }
    }
)

$groups = @(
    Get-LocalGroup |
    Sort-Object Name |
    ForEach-Object {
        $group = $_
        $members = @()

        try {
            $members = @(
                Get-LocalGroupMember -Group $group.Name -ErrorAction Stop |
                Sort-Object Name |
                ForEach-Object {
                    [pscustomobject]@{
                        name = $_.Name
                        objectClass = if ($_.ObjectClass) { $_.ObjectClass.ToString() } else { $null }
                        principalSource = if ($_.PrincipalSource) { $_.PrincipalSource.ToString() } else { $null }
                        sid = if ($_.SID) { $_.SID.Value } else { $null }
                    }
                }
            )
        }
        catch {
            $members = @()
        }

        [pscustomobject]@{
            name = $group.Name
            description = $group.Description
            sid = if ($group.SID) { $group.SID.Value } else { $null }
            members = $members
        }
    }
)

[pscustomobject]@{
    users = $users
    groups = $groups
} | ConvertTo-Json -Depth 8 -Compress
""";
}
