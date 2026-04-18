namespace InfraPilot.Central.Tests;

using System.Text.Json;
using InfraPilot.Central.Application;
using InfraPilot.Central.Infrastructure.Sqlite;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Common;
using InfraPilot.Contracts.Services;
using Microsoft.Extensions.Options;
using Xunit;

public sealed class SqliteCentralStoreTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _databaseDirectory;

    public SqliteCentralStoreTests()
    {
        _databaseDirectory = Path.Combine(Path.GetTempPath(), "InfraPilot.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_databaseDirectory);
    }

    [Fact]
    public async Task Snapshot_history_and_change_feed_are_generated_for_capability_changes()
    {
        var store = CreateStore();
        var agent = await CreateApprovedAgentAsync(store);

        await store.ReplaceCapabilitiesAsync(
            agent.AgentId,
            [
                new CapabilityDescriptorDto(
                    CapabilityKeys.Services,
                    "Windows Services",
                    "1.0.0",
                    [])
            ],
            CancellationToken.None);

        await store.AddSnapshotsAsync(
            agent.AgentId,
            DateTimeOffset.Parse("2026-04-17T10:00:00Z"),
            [
                CreateServiceSnapshot(
                    "hash-1",
                    new ServiceStatusDto("Spooler", "Print Spooler", "Running", "True", "Win32OwnProcess"))
            ],
            CancellationToken.None);

        await store.AddSnapshotsAsync(
            agent.AgentId,
            DateTimeOffset.Parse("2026-04-17T10:05:00Z"),
            [
                CreateServiceSnapshot(
                    "hash-2",
                    new ServiceStatusDto("Spooler", "Print Spooler", "Stopped", "True", "Win32OwnProcess"))
            ],
            CancellationToken.None);

        var history = await store.GetCapabilityHistoryAsync(agent.AgentId, CapabilityKeys.Services, 10, CancellationToken.None);
        var detail = await store.GetAgentDetailAsync(agent.AgentId, CancellationToken.None);
        var feed = await store.GetChangeFeedAsync(agent.AgentId, 10, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(2, history.Count);
        Assert.True(history[0].ChangeSummary.HasChanges);
        Assert.Contains("state changes", history[0].ChangeSummary.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Spooler", string.Join(" | ", history[0].ChangeSummary.Highlights), StringComparison.OrdinalIgnoreCase);
        Assert.False(history[1].ChangeSummary.HasPreviousSnapshot);

        Assert.Equal(2, feed.Count);
        Assert.Equal("SnapshotChanged", feed[0].ChangeKind);
        Assert.Equal("InitialSnapshot", feed[1].ChangeKind);

        var servicesCapability = Assert.Single(detail!.Capabilities, capability =>
            string.Equals(capability.Descriptor.CapabilityKey, CapabilityKeys.Services, StringComparison.OrdinalIgnoreCase));
        Assert.True(servicesCapability.ChangeSummary.HasChanges);
        Assert.Equal(2, detail.RecentChanges.Count);
    }

    [Fact]
    public async Task Late_action_results_are_rejected_and_marked_as_timed_out()
    {
        var store = CreateStore();
        var agent = await CreateApprovedAgentAsync(store);

        var createdAction = await store.CreateActionAsync(
            new ActionCommandCreateRequestDto(
                agent.AgentId,
                CapabilityKeys.Services,
                "restart",
                "Spooler",
                null,
                "tests"),
            CancellationToken.None);

        var leased = await store.LeaseNextActionAsync(agent.AgentId, TimeSpan.FromMilliseconds(20), CancellationToken.None);
        Assert.NotNull(leased);

        await Task.Delay(120);

        var completed = await store.CompleteActionAsync(
            agent.AgentId,
            createdAction.ActionId,
            new AgentActionResultReportDto(
                agent.InstallationId,
                ActionStatuses.Succeeded,
                "Completed late.",
                null,
                null,
                DateTimeOffset.UtcNow),
            CancellationToken.None);

        var detail = await store.GetAgentDetailAsync(agent.AgentId, CancellationToken.None);
        var action = Assert.Single(detail!.RecentActions);

        Assert.False(completed);
        Assert.Equal(ActionStatuses.TimedOut, action.Status);
        Assert.Equal(1, action.AttemptCount);
    }

    [Fact]
    public async Task Pending_actions_can_be_cancelled_before_they_are_leased()
    {
        var store = CreateStore();
        var agent = await CreateApprovedAgentAsync(store);

        var createdAction = await store.CreateActionAsync(
            new ActionCommandCreateRequestDto(
                agent.AgentId,
                CapabilityKeys.ScheduledTasks,
                "run",
                @"\Prueba\demo",
                null,
                "tests"),
            CancellationToken.None);

        var cancelled = await store.CancelPendingActionAsync(createdAction.ActionId, "tests", "No longer needed.", CancellationToken.None);

        Assert.NotNull(cancelled);
        Assert.Equal(ActionStatuses.Cancelled, cancelled!.Status);
        Assert.Contains("Cancelled by tests", cancelled.ResultMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, cancelled.AttemptCount);
    }

    [Fact]
    public async Task Agents_can_be_revoked_and_their_token_rotated()
    {
        var store = CreateStore();
        var agent = await CreateApprovedAgentAsync(store);

        var revoked = await store.RevokeAgentAsync(agent.AgentId, CancellationToken.None);
        var reloaded = await store.GetAgentByInstallationIdAsync(agent.InstallationId, CancellationToken.None);

        Assert.NotNull(revoked);
        Assert.NotNull(reloaded);
        Assert.Equal(AgentStatuses.Revoked, revoked!.Status);
        Assert.Equal(AgentStatuses.Revoked, reloaded!.Status);
        Assert.NotEqual(agent.AccessToken, revoked.AccessToken);
    }

    [Fact]
    public async Task Resetting_agent_token_keeps_agent_but_rotates_credentials()
    {
        var store = CreateStore();
        var agent = await CreateApprovedAgentAsync(store);

        var reset = await store.ResetAgentTokenAsync(agent.AgentId, CancellationToken.None);
        var reloaded = await store.GetAgentByInstallationIdAsync(agent.InstallationId, CancellationToken.None);

        Assert.NotNull(reset);
        Assert.NotNull(reloaded);
        Assert.Equal(AgentStatuses.Approved, reset!.Status);
        Assert.NotEqual(agent.AccessToken, reset.AccessToken);
        Assert.Equal(reset.AccessToken, reloaded!.AccessToken);
    }

    [Fact]
    public async Task Retention_cleanup_removes_old_snapshots_and_change_events()
    {
        var store = CreateStore();
        var agent = await CreateApprovedAgentAsync(store);

        await store.ReplaceCapabilitiesAsync(
            agent.AgentId,
            [
                new CapabilityDescriptorDto(
                    CapabilityKeys.Services,
                    "Windows Services",
                    "1.0.0",
                    [])
            ],
            CancellationToken.None);

        await store.AddSnapshotsAsync(
            agent.AgentId,
            DateTimeOffset.Parse("2026-04-10T10:00:00Z"),
            [
                CreateServiceSnapshot(
                    "hash-old-1",
                    new ServiceStatusDto("Spooler", "Print Spooler", "Running", "True", "Win32OwnProcess"))
            ],
            CancellationToken.None);

        await store.AddSnapshotsAsync(
            agent.AgentId,
            DateTimeOffset.Parse("2026-04-10T10:05:00Z"),
            [
                CreateServiceSnapshot(
                    "hash-old-2",
                    new ServiceStatusDto("Spooler", "Print Spooler", "Stopped", "True", "Win32OwnProcess"))
            ],
            CancellationToken.None);

        await store.AddSnapshotsAsync(
            agent.AgentId,
            DateTimeOffset.Parse("2026-04-18T10:05:00Z"),
            [
                CreateServiceSnapshot(
                    "hash-new-1",
                    new ServiceStatusDto("Spooler", "Print Spooler", "Running", "True", "Win32OwnProcess"))
            ],
            CancellationToken.None);

        var cleanup = await store.CleanupExpiredDataAsync(
            DateTimeOffset.Parse("2026-04-17T00:00:00Z"),
            DateTimeOffset.Parse("2026-04-17T00:00:00Z"),
            DateTimeOffset.Parse("2026-04-17T00:00:00Z"),
            CancellationToken.None);

        var history = await store.GetCapabilityHistoryAsync(agent.AgentId, CapabilityKeys.Services, 10, CancellationToken.None);
        var changes = await store.GetChangeFeedAsync(agent.AgentId, 10, CancellationToken.None);

        Assert.True(cleanup.SnapshotsDeleted >= 2);
        Assert.True(cleanup.ChangeEventsDeleted >= 2);
        Assert.Single(history);
        Assert.Single(changes);
        Assert.Equal("hash-new-1", history[0].Hash);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_databaseDirectory))
        {
            return;
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Directory.Delete(_databaseDirectory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 2)
            {
                Thread.Sleep(50);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private SqliteCentralStore CreateStore()
    {
        var store = new SqliteCentralStore(Options.Create(new CentralOptions
        {
            DatabasePath = Path.Combine(_databaseDirectory, "infrapilot.db")
        }));

        store.InitializeAsync(CancellationToken.None).GetAwaiter().GetResult();
        return store;
    }

    private static async Task<StoredAgent> CreateApprovedAgentAsync(SqliteCentralStore store)
    {
        var agent = new StoredAgent(
            Guid.NewGuid(),
            $"installation-{Guid.NewGuid():N}",
            "Agent-01",
            "SERVER-01",
            AgentStatuses.Approved,
            "0.1.0",
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        await store.UpsertAgentAsync(agent, CancellationToken.None);
        return agent;
    }

    private static CapabilitySnapshotDto CreateServiceSnapshot(string hash, params ServiceStatusDto[] services)
        => new(
            CapabilityKeys.Services,
            "1.0.0",
            hash,
            JsonSerializer.Serialize(new ServiceSnapshotDto(services), JsonOptions));
}
