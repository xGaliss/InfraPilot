namespace InfraPilot.Capabilities.Iis.Windows;

using InfraPilot.Capabilities.Abstractions;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Iis;
using Microsoft.Extensions.Options;
using Microsoft.Web.Administration;

public sealed class WindowsIisCapabilityModule : ICapabilityModule
{
    private readonly IisCapabilityOptions _options;

    private static readonly CapabilityDescriptorDto Descriptor = new(
        CapabilityKeys.Iis,
        "IIS",
        "1.0.0",
        [
            new CapabilityActionDefinitionDto("appPool.start", "Start app pool", true),
            new CapabilityActionDefinitionDto("appPool.stop", "Stop app pool", true),
            new CapabilityActionDefinitionDto("appPool.recycle", "Recycle app pool", true),
            new CapabilityActionDefinitionDto("site.start", "Start site", true),
            new CapabilityActionDefinitionDto("site.stop", "Stop site", true)
        ]);

    public WindowsIisCapabilityModule(IOptions<IisCapabilityOptions> options)
    {
        _options = options.Value;
    }

    public CapabilityDescriptorDto Describe() => Descriptor;

    public Task<CapabilitySnapshotResult> CollectSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var serverManager = new ServerManager();
            var appPools = serverManager.ApplicationPools
                .Where(pool => CapabilityFilter.Matches(pool.Name, _options.IncludeAppPools, _options.ExcludeAppPools))
                .Select(pool => new IisAppPoolDto(pool.Name, pool.State.ToString()))
                .OrderBy(pool => pool.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var sites = serverManager.Sites
                .Where(site => CapabilityFilter.Matches(site.Name, _options.IncludeSites, _options.ExcludeSites))
                .Select(site => new IisSiteDto(
                    site.Name,
                    site.State.ToString(),
                    site.Bindings.Select(binding => binding.BindingInformation).ToList()))
                .OrderBy(site => site.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult(new CapabilitySnapshotResult(
                CapabilityKeys.Iis,
                "1.0.0",
                new IisSnapshotDto(appPools, sites)));
        }
        catch
        {
            return Task.FromResult(new CapabilitySnapshotResult(
                CapabilityKeys.Iis,
                "1.0.0",
                new IisSnapshotDto([], [])));
        }
    }

    public Task<CapabilityActionExecutionResult> ExecuteActionAsync(
        AgentActionCommandDto command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.TargetKey))
        {
            return Task.FromResult(new CapabilityActionExecutionResult(false, "An IIS target is required.", "Missing target."));
        }

        try
        {
            using var serverManager = new ServerManager();

            return Task.FromResult(command.ActionKey switch
            {
                "appPool.start" => StartAppPool(serverManager, command.TargetKey),
                "appPool.stop" => StopAppPool(serverManager, command.TargetKey),
                "appPool.recycle" => RecycleAppPool(serverManager, command.TargetKey),
                "site.start" => StartSite(serverManager, command.TargetKey),
                "site.stop" => StopSite(serverManager, command.TargetKey),
                _ => new CapabilityActionExecutionResult(false, $"Unsupported action '{command.ActionKey}'.", "Unsupported action.")
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CapabilityActionExecutionResult(false, ex.Message, ex.ToString()));
        }
    }

    private static CapabilityActionExecutionResult StartAppPool(ServerManager serverManager, string target)
    {
        var pool = serverManager.ApplicationPools[target];
        if (pool is null)
        {
            return new CapabilityActionExecutionResult(false, $"App pool '{target}' was not found.", "Target not found.");
        }

        var result = pool.Start();
        return new CapabilityActionExecutionResult(true, $"App pool '{target}' start requested. Result={result}.");
    }

    private static CapabilityActionExecutionResult StopAppPool(ServerManager serverManager, string target)
    {
        var pool = serverManager.ApplicationPools[target];
        if (pool is null)
        {
            return new CapabilityActionExecutionResult(false, $"App pool '{target}' was not found.", "Target not found.");
        }

        var result = pool.Stop();
        return new CapabilityActionExecutionResult(true, $"App pool '{target}' stop requested. Result={result}.");
    }

    private static CapabilityActionExecutionResult RecycleAppPool(ServerManager serverManager, string target)
    {
        var pool = serverManager.ApplicationPools[target];
        if (pool is null)
        {
            return new CapabilityActionExecutionResult(false, $"App pool '{target}' was not found.", "Target not found.");
        }

        pool.Recycle();
        return new CapabilityActionExecutionResult(true, $"App pool '{target}' recycled.");
    }

    private static CapabilityActionExecutionResult StartSite(ServerManager serverManager, string target)
    {
        var site = serverManager.Sites[target];
        if (site is null)
        {
            return new CapabilityActionExecutionResult(false, $"Site '{target}' was not found.", "Target not found.");
        }

        var result = site.Start();
        return new CapabilityActionExecutionResult(true, $"Site '{target}' start requested. Result={result}.");
    }

    private static CapabilityActionExecutionResult StopSite(ServerManager serverManager, string target)
    {
        var site = serverManager.Sites[target];
        if (site is null)
        {
            return new CapabilityActionExecutionResult(false, $"Site '{target}' was not found.", "Target not found.");
        }

        var result = site.Stop();
        return new CapabilityActionExecutionResult(true, $"Site '{target}' stop requested. Result={result}.");
    }
}
