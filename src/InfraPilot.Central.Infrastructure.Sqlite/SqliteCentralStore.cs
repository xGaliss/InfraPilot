namespace InfraPilot.Central.Infrastructure.Sqlite;

using System.Text.Json;
using InfraPilot.Central.Application;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Changes;
using InfraPilot.Contracts.Common;
using InfraPilot.Contracts.FileTree;
using InfraPilot.Contracts.Iis;
using InfraPilot.Contracts.ScheduledTasks;
using InfraPilot.Contracts.Services;
using InfraPilot.Contracts.UsersAndGroups;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

public sealed class SqliteCentralStore : ICentralStore
{
    private readonly string _connectionString;
    private readonly CentralOptions _options;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SqliteCentralStore(IOptions<CentralOptions> options)
    {
        _options = options.Value;
        var databasePath = Path.GetFullPath(_options.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = $"Data Source={databasePath}";
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;

            CREATE TABLE IF NOT EXISTS Agents (
                AgentId TEXT PRIMARY KEY,
                InstallationId TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                MachineName TEXT NOT NULL,
                Status TEXT NOT NULL,
                AgentVersion TEXT NOT NULL,
                AccessToken TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                ApprovedUtc TEXT NULL,
                LastSeenUtc TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS AgentCapabilities (
                AgentId TEXT NOT NULL,
                CapabilityKey TEXT NOT NULL,
                DisplayName TEXT NOT NULL,
                Version TEXT NOT NULL,
                ActionsJson TEXT NOT NULL,
                PublishedUtc TEXT NOT NULL,
                PRIMARY KEY (AgentId, CapabilityKey)
            );

            CREATE TABLE IF NOT EXISTS CapabilitySnapshots (
                SnapshotId TEXT PRIMARY KEY,
                AgentId TEXT NOT NULL,
                CapabilityKey TEXT NOT NULL,
                CollectedUtc TEXT NOT NULL,
                Hash TEXT NOT NULL,
                PayloadJson TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CapabilityChangeEvents (
                ChangeEventId TEXT PRIMARY KEY,
                AgentId TEXT NOT NULL,
                CapabilityKey TEXT NOT NULL,
                SnapshotId TEXT NOT NULL,
                PreviousSnapshotId TEXT NULL,
                ChangeKind TEXT NOT NULL,
                CollectedUtc TEXT NOT NULL,
                PreviousCollectedUtc TEXT NULL,
                Summary TEXT NOT NULL,
                HighlightsJson TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ActionCommands (
                ActionId TEXT PRIMARY KEY,
                AgentId TEXT NOT NULL,
                CapabilityKey TEXT NOT NULL,
                ActionKey TEXT NOT NULL,
                TargetKey TEXT NULL,
                PayloadJson TEXT NULL,
                Status TEXT NOT NULL,
                RequestedBy TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL,
                LeasedUtc TEXT NULL,
                LeaseExpiresUtc TEXT NULL,
                CompletedUtc TEXT NULL,
                AttemptCount INTEGER NOT NULL DEFAULT 0,
                ResultMessage TEXT NULL,
                ErrorMessage TEXT NULL,
                OutputJson TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_CapabilitySnapshots_Agent_Capability_CollectedUtc
                ON CapabilitySnapshots (AgentId, CapabilityKey, CollectedUtc DESC);

            CREATE INDEX IF NOT EXISTS IX_CapabilityChangeEvents_Agent_CollectedUtc
                ON CapabilityChangeEvents (AgentId, CollectedUtc DESC);

            CREATE INDEX IF NOT EXISTS IX_CapabilityChangeEvents_Agent_Capability_CollectedUtc
                ON CapabilityChangeEvents (AgentId, CapabilityKey, CollectedUtc DESC);

            CREATE INDEX IF NOT EXISTS IX_ActionCommands_Agent_Status_CreatedUtc
                ON ActionCommands (AgentId, Status, CreatedUtc);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureColumnAsync(connection, "ActionCommands", "AttemptCount", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
    }

    public async Task<StoredAgent?> GetAgentByInstallationIdAsync(string installationId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT AgentId, InstallationId, DisplayName, MachineName, Status, AgentVersion, AccessToken, CreatedUtc, ApprovedUtc, LastSeenUtc
            FROM Agents
            WHERE InstallationId = $installationId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$installationId", installationId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadStoredAgent(reader);
    }

    public async Task UpsertAgentAsync(StoredAgent agent, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Agents (
                AgentId, InstallationId, DisplayName, MachineName, Status, AgentVersion, AccessToken, CreatedUtc, ApprovedUtc, LastSeenUtc
            ) VALUES (
                $agentId, $installationId, $displayName, $machineName, $status, $agentVersion, $accessToken, $createdUtc, $approvedUtc, $lastSeenUtc
            )
            ON CONFLICT(InstallationId) DO UPDATE SET
                DisplayName = excluded.DisplayName,
                MachineName = excluded.MachineName,
                Status = excluded.Status,
                AgentVersion = excluded.AgentVersion,
                AccessToken = excluded.AccessToken,
                ApprovedUtc = excluded.ApprovedUtc,
                LastSeenUtc = excluded.LastSeenUtc;
            """;

        BindAgentParameters(command, agent);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task TouchHeartbeatAsync(Guid agentId, string agentVersion, DateTimeOffset lastSeenUtc, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Agents
            SET AgentVersion = $agentVersion,
                LastSeenUtc = $lastSeenUtc
            WHERE AgentId = $agentId;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$agentVersion", agentVersion);
        command.Parameters.AddWithValue("$lastSeenUtc", ToDb(lastSeenUtc));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ReplaceCapabilitiesAsync(Guid agentId, IReadOnlyList<CapabilityDescriptorDto> capabilities, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM AgentCapabilities WHERE AgentId = $agentId;";
            deleteCommand.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var capability in capabilities)
        {
            await using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO AgentCapabilities (AgentId, CapabilityKey, DisplayName, Version, ActionsJson, PublishedUtc)
                VALUES ($agentId, $capabilityKey, $displayName, $version, $actionsJson, $publishedUtc);
                """;
            insertCommand.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            insertCommand.Parameters.AddWithValue("$capabilityKey", capability.CapabilityKey);
            insertCommand.Parameters.AddWithValue("$displayName", capability.DisplayName);
            insertCommand.Parameters.AddWithValue("$version", capability.Version);
            insertCommand.Parameters.AddWithValue("$actionsJson", JsonSerializer.Serialize(capability.Actions, JsonOptions));
            insertCommand.Parameters.AddWithValue("$publishedUtc", ToDb(DateTimeOffset.UtcNow));
            await insertCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddSnapshotsAsync(Guid agentId, DateTimeOffset collectedUtc, IReadOnlyList<CapabilitySnapshotDto> snapshots, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var previousSnapshot = await GetLatestCapabilitySnapshotAsync(
                connection,
                transaction,
                agentId,
                snapshot.CapabilityKey,
                cancellationToken);
            var snapshotId = Guid.NewGuid();

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO CapabilitySnapshots (SnapshotId, AgentId, CapabilityKey, CollectedUtc, Hash, PayloadJson)
                VALUES ($snapshotId, $agentId, $capabilityKey, $collectedUtc, $hash, $payloadJson);
                """;
            command.Parameters.AddWithValue("$snapshotId", snapshotId.ToString("D"));
            command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            command.Parameters.AddWithValue("$capabilityKey", snapshot.CapabilityKey);
            command.Parameters.AddWithValue("$collectedUtc", ToDb(collectedUtc));
            command.Parameters.AddWithValue("$hash", snapshot.Hash);
            command.Parameters.AddWithValue("$payloadJson", snapshot.PayloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken);

            var latestSnapshot = new CapabilitySnapshotEnvelope(
                snapshotId,
                collectedUtc,
                snapshot.Hash,
                snapshot.PayloadJson);
            var changeSummary = BuildChangeSummary(snapshot.CapabilityKey, latestSnapshot, previousSnapshot);
            var changeEvent = BuildChangeEvent(agentId, snapshot.CapabilityKey, latestSnapshot, previousSnapshot, changeSummary);
            if (changeEvent is not null)
            {
                await InsertChangeEventAsync(connection, transaction, latestSnapshot, previousSnapshot, changeEvent, cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<AgentActionCommandDto?> LeaseNextActionAsync(Guid agentId, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        await using (var timeoutCommand = connection.CreateCommand())
        {
            timeoutCommand.Transaction = transaction;
            timeoutCommand.CommandText =
                """
                UPDATE ActionCommands
                SET Status = $timedOut,
                    CompletedUtc = $completedUtc,
                    ErrorMessage = COALESCE(ErrorMessage, 'Lease expired before the agent reported a final status.')
                WHERE AgentId = $agentId
                  AND Status = $inProgress
                  AND LeaseExpiresUtc IS NOT NULL
                  AND LeaseExpiresUtc < $now;
                """;
            timeoutCommand.Parameters.AddWithValue("$timedOut", ActionStatuses.TimedOut);
            timeoutCommand.Parameters.AddWithValue("$completedUtc", ToDb(now));
            timeoutCommand.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            timeoutCommand.Parameters.AddWithValue("$inProgress", ActionStatuses.InProgress);
            timeoutCommand.Parameters.AddWithValue("$now", ToDb(now));
            await timeoutCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        Guid? actionId = null;
        string? capabilityKey = null;
        string? actionKey = null;
        string? targetKey = null;
        string? payloadJson = null;
        string? requestedBy = null;
        DateTimeOffset createdUtc = default;

        await using (var selectCommand = connection.CreateCommand())
        {
            selectCommand.Transaction = transaction;
            selectCommand.CommandText =
                """
                SELECT ActionId, CapabilityKey, ActionKey, TargetKey, PayloadJson, RequestedBy, CreatedUtc
                FROM ActionCommands
                WHERE AgentId = $agentId
                  AND Status = $pending
                ORDER BY CreatedUtc
                LIMIT 1;
                """;
            selectCommand.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            selectCommand.Parameters.AddWithValue("$pending", ActionStatuses.Pending);

            await using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                actionId = Guid.Parse(reader.GetString(0));
                capabilityKey = reader.GetString(1);
                actionKey = reader.GetString(2);
                targetKey = reader.IsDBNull(3) ? null : reader.GetString(3);
                payloadJson = reader.IsDBNull(4) ? null : reader.GetString(4);
                requestedBy = reader.GetString(5);
                createdUtc = FromDb(reader.GetString(6));
            }
        }

        if (actionId is null || capabilityKey is null || actionKey is null || requestedBy is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        await using (var updateCommand = connection.CreateCommand())
        {
            updateCommand.Transaction = transaction;
            updateCommand.CommandText =
                """
                UPDATE ActionCommands
                SET Status = $status,
                    LeasedUtc = $leasedUtc,
                    LeaseExpiresUtc = $leaseExpiresUtc,
                    AttemptCount = AttemptCount + 1
                WHERE ActionId = $actionId;
                """;
            updateCommand.Parameters.AddWithValue("$status", ActionStatuses.InProgress);
            updateCommand.Parameters.AddWithValue("$leasedUtc", ToDb(now));
            updateCommand.Parameters.AddWithValue("$leaseExpiresUtc", ToDb(now.Add(leaseDuration)));
            updateCommand.Parameters.AddWithValue("$actionId", actionId.Value.ToString("D"));
            await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return new AgentActionCommandDto(
            actionId.Value,
            capabilityKey,
            actionKey,
            targetKey,
            payloadJson,
            requestedBy,
            createdUtc);
    }

    public async Task<bool> CompleteActionAsync(Guid agentId, Guid actionId, AgentActionResultReportDto result, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        await using (var timeoutCommand = connection.CreateCommand())
        {
            timeoutCommand.CommandText =
                """
                UPDATE ActionCommands
                SET Status = $timedOut,
                    CompletedUtc = $completedUtc,
                    ErrorMessage = COALESCE(ErrorMessage, 'Lease expired before the agent reported a final status.')
                WHERE ActionId = $actionId
                  AND AgentId = $agentId
                  AND Status = $inProgress
                  AND LeaseExpiresUtc IS NOT NULL
                  AND LeaseExpiresUtc < $now;
                """;
            timeoutCommand.Parameters.AddWithValue("$timedOut", ActionStatuses.TimedOut);
            timeoutCommand.Parameters.AddWithValue("$completedUtc", ToDb(now));
            timeoutCommand.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
            timeoutCommand.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            timeoutCommand.Parameters.AddWithValue("$inProgress", ActionStatuses.InProgress);
            timeoutCommand.Parameters.AddWithValue("$now", ToDb(now));
            await timeoutCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE ActionCommands
            SET Status = $status,
                CompletedUtc = $completedUtc,
                ResultMessage = $resultMessage,
                ErrorMessage = $errorMessage,
                OutputJson = $outputJson
            WHERE ActionId = $actionId
              AND AgentId = $agentId
              AND Status = $inProgress
              AND (LeaseExpiresUtc IS NULL OR LeaseExpiresUtc >= $now);
            """;
        command.Parameters.AddWithValue("$status", result.Status);
        command.Parameters.AddWithValue("$completedUtc", ToDb(result.CompletedUtc));
        command.Parameters.AddWithValue("$resultMessage", (object?)result.ResultMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)result.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputJson", (object?)result.OutputJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$inProgress", ActionStatuses.InProgress);
        command.Parameters.AddWithValue("$now", ToDb(now));
        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> HasQueuedActionAsync(
        Guid agentId,
        string capabilityKey,
        string actionKey,
        string? targetKey,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(1)
            FROM ActionCommands
            WHERE AgentId = $agentId
              AND CapabilityKey = $capabilityKey
              AND ActionKey = $actionKey
              AND (($targetKey IS NULL AND TargetKey IS NULL) OR TargetKey = $targetKey)
              AND Status IN ($pending, $inProgress);
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$capabilityKey", capabilityKey);
        command.Parameters.AddWithValue("$actionKey", actionKey);
        command.Parameters.AddWithValue("$targetKey", (object?)targetKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$pending", ActionStatuses.Pending);
        command.Parameters.AddWithValue("$inProgress", ActionStatuses.InProgress);

        var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        return count > 0;
    }

    public async Task<IReadOnlyList<AgentListItemDto>> GetAgentsAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var agents = new List<AgentListItemDto>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT AgentId, InstallationId, DisplayName, MachineName, Status, AgentVersion, CreatedUtc, ApprovedUtc, LastSeenUtc
                FROM Agents
                ORDER BY DisplayName, MachineName;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                agents.Add(new AgentListItemDto(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    AgentHealthStatuses.Unknown,
                    reader.GetString(5),
                    FromDb(reader.GetString(6)),
                    reader.IsDBNull(7) ? null : FromDb(reader.GetString(7)),
                    reader.IsDBNull(8) ? null : FromDb(reader.GetString(8)),
                    null,
                    [],
                    null,
                    null,
                    0,
                    0,
                    0));
            }
        }

        var capabilityLookup = await GetCapabilityKeysAsync(connection, cancellationToken);
        var latestActionLookup = await GetLatestActionLookupAsync(connection, cancellationToken);
        var actionStatsLookup = await GetActionQueueStatsAsync(connection, cancellationToken);
        var latestSnapshotLookup = await GetLatestSnapshotLookupAsync(connection, cancellationToken);

        return agents
            .Select(agent =>
            {
                capabilityLookup.TryGetValue(agent.AgentId, out var capabilityKeys);
                latestActionLookup.TryGetValue(agent.AgentId, out var latestAction);
                actionStatsLookup.TryGetValue(agent.AgentId, out var actionStats);
                latestSnapshotLookup.TryGetValue(agent.AgentId, out var lastCollectedUtc);

                return agent with
                {
                    HealthStatus = AgentHealthEvaluator.Compute(agent.Status, agent.LastSeenUtc, _options, now),
                    LastCollectedUtc = lastCollectedUtc,
                    CapabilityKeys = capabilityKeys ?? [],
                    LastActionStatus = latestAction?.Status,
                    LastActionSummary = latestAction?.ResultMessage ?? latestAction?.ErrorMessage,
                    PendingActionCount = actionStats?.PendingCount ?? 0,
                    InProgressActionCount = actionStats?.InProgressCount ?? 0,
                    FailedActionCount = actionStats?.FailedCount ?? 0
                };
            })
            .ToList();
    }

    public async Task<AgentDetailDto?> GetAgentDetailAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        StoredAgent? agent = null;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT AgentId, InstallationId, DisplayName, MachineName, Status, AgentVersion, AccessToken, CreatedUtc, ApprovedUtc, LastSeenUtc
                FROM Agents
                WHERE AgentId = $agentId
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                agent = ReadStoredAgent(reader);
            }
        }

        if (agent is null)
        {
            return null;
        }

        var capabilities = await GetCapabilityStatesAsync(connection, agentId, cancellationToken);
        var recentActions = await GetRecentActionsAsync(connection, agentId, cancellationToken);
        var recentChanges = await GetRecentChangeEventsAsync(connection, agentId, 20, cancellationToken);
        var latestSnapshotLookup = await GetLatestSnapshotLookupAsync(connection, cancellationToken);
        var actionStatsLookup = await GetActionQueueStatsAsync(connection, cancellationToken);
        latestSnapshotLookup.TryGetValue(agentId, out var lastCollectedUtc);
        actionStatsLookup.TryGetValue(agentId, out var actionStats);

        return new AgentDetailDto(
            agent.AgentId,
            agent.InstallationId,
            agent.DisplayName,
            agent.MachineName,
            agent.Status,
            AgentHealthEvaluator.Compute(agent.Status, agent.LastSeenUtc, _options, now),
            agent.AgentVersion,
            agent.CreatedUtc,
            agent.ApprovedUtc,
            agent.LastSeenUtc,
            lastCollectedUtc,
            actionStats?.PendingCount ?? 0,
            actionStats?.InProgressCount ?? 0,
            actionStats?.FailedCount ?? 0,
            capabilities,
            recentActions,
            recentChanges);
    }

    public async Task<bool> ApproveAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Agents
            SET Status = $status,
                ApprovedUtc = $approvedUtc
            WHERE AgentId = $agentId;
            """;
        command.Parameters.AddWithValue("$status", AgentStatuses.Approved);
        command.Parameters.AddWithValue("$approvedUtc", ToDb(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<ActionCommandSummaryDto> CreateActionAsync(ActionCommandCreateRequestDto request, CancellationToken cancellationToken)
    {
        var actionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ActionCommands (
                ActionId, AgentId, CapabilityKey, ActionKey, TargetKey, PayloadJson, Status, RequestedBy, CreatedUtc, AttemptCount
            ) VALUES (
                $actionId, $agentId, $capabilityKey, $actionKey, $targetKey, $payloadJson, $status, $requestedBy, $createdUtc, $attemptCount
            );
            """;
        command.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
        command.Parameters.AddWithValue("$agentId", request.AgentId.ToString("D"));
        command.Parameters.AddWithValue("$capabilityKey", request.CapabilityKey);
        command.Parameters.AddWithValue("$actionKey", request.ActionKey);
        command.Parameters.AddWithValue("$targetKey", (object?)request.TargetKey ?? DBNull.Value);
        command.Parameters.AddWithValue("$payloadJson", (object?)request.PayloadJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", ActionStatuses.Pending);
        command.Parameters.AddWithValue("$requestedBy", request.RequestedBy);
        command.Parameters.AddWithValue("$createdUtc", ToDb(now));
        command.Parameters.AddWithValue("$attemptCount", 0);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return new ActionCommandSummaryDto(
            actionId,
            request.AgentId,
            request.CapabilityKey,
            request.ActionKey,
            request.TargetKey,
            ActionStatuses.Pending,
            request.RequestedBy,
            now,
            null,
            null,
            null,
            0,
            null,
            null);
    }

    public async Task<IReadOnlyList<CapabilitySnapshotHistoryItemDto>> GetCapabilityHistoryAsync(
        Guid agentId,
        string capabilityKey,
        int take,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var snapshots = await GetCapabilitySnapshotHistoryInternalAsync(connection, agentId, capabilityKey, take, cancellationToken);
        return snapshots;
    }

    public async Task<IReadOnlyList<CapabilityChangeEventDto>> GetChangeFeedAsync(
        Guid? agentId,
        int take,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        return await GetRecentChangeEventsAsync(connection, agentId, take, cancellationToken);
    }

    public async Task<ActionCommandSummaryDto?> CancelPendingActionAsync(
        Guid actionId,
        string requestedBy,
        string? reason,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenConnectionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var summary = string.IsNullOrWhiteSpace(reason)
            ? $"Cancelled by {requestedBy}."
            : $"Cancelled by {requestedBy}. {reason.Trim()}";

        await using var updateCommand = connection.CreateCommand();
        updateCommand.CommandText =
            """
            UPDATE ActionCommands
            SET Status = $status,
                CompletedUtc = $completedUtc,
                ResultMessage = $resultMessage
            WHERE ActionId = $actionId
              AND Status = $pending;
            """;
        updateCommand.Parameters.AddWithValue("$status", ActionStatuses.Cancelled);
        updateCommand.Parameters.AddWithValue("$completedUtc", ToDb(now));
        updateCommand.Parameters.AddWithValue("$resultMessage", summary);
        updateCommand.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
        updateCommand.Parameters.AddWithValue("$pending", ActionStatuses.Pending);

        if (await updateCommand.ExecuteNonQueryAsync(cancellationToken) == 0)
        {
            return null;
        }

        return await GetActionSummaryByIdAsync(connection, actionId, cancellationToken);
    }

    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetCapabilityKeysAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, IReadOnlyList<string>>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT AgentId, CapabilityKey
            FROM AgentCapabilities
            ORDER BY CapabilityKey;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var temp = new Dictionary<Guid, List<string>>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var agentId = Guid.Parse(reader.GetString(0));
            if (!temp.TryGetValue(agentId, out var keys))
            {
                keys = [];
                temp[agentId] = keys;
            }

            keys.Add(reader.GetString(1));
        }

        foreach (var (key, value) in temp)
        {
            result[key] = value;
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, ActionCommandSummaryDto>> GetLatestActionLookupAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ActionCommandSummaryDto>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ActionId, AgentId, CapabilityKey, ActionKey, TargetKey, Status, RequestedBy, CreatedUtc, LeasedUtc, LeaseExpiresUtc, CompletedUtc, AttemptCount, ResultMessage, ErrorMessage
            FROM ActionCommands
            ORDER BY CreatedUtc DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var agentId = Guid.Parse(reader.GetString(1));
            if (result.ContainsKey(agentId))
            {
                continue;
            }

            result[agentId] = new ActionCommandSummaryDto(
                Guid.Parse(reader.GetString(0)),
                agentId,
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                FromDb(reader.GetString(7)),
                reader.IsDBNull(8) ? null : FromDb(reader.GetString(8)),
                reader.IsDBNull(9) ? null : FromDb(reader.GetString(9)),
                reader.IsDBNull(10) ? null : FromDb(reader.GetString(10)),
                reader.IsDBNull(11) ? 0 : Convert.ToInt32(reader.GetValue(11)),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13));
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, ActionQueueStats>> GetActionQueueStatsAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, ActionQueueStats>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                AgentId,
                SUM(CASE WHEN Status = $pending THEN 1 ELSE 0 END) AS PendingCount,
                SUM(CASE WHEN Status = $inProgress THEN 1 ELSE 0 END) AS InProgressCount,
                SUM(CASE WHEN Status = $failed THEN 1 ELSE 0 END) AS FailedCount
            FROM ActionCommands
            GROUP BY AgentId;
            """;
        command.Parameters.AddWithValue("$pending", ActionStatuses.Pending);
        command.Parameters.AddWithValue("$inProgress", ActionStatuses.InProgress);
        command.Parameters.AddWithValue("$failed", ActionStatuses.Failed);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[Guid.Parse(reader.GetString(0))] = new ActionQueueStats(
                reader.IsDBNull(1) ? 0 : Convert.ToInt32(reader.GetValue(1)),
                reader.IsDBNull(2) ? 0 : Convert.ToInt32(reader.GetValue(2)),
                reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3)));
        }

        return result;
    }

    private async Task<IReadOnlyDictionary<Guid, DateTimeOffset?>> GetLatestSnapshotLookupAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, DateTimeOffset?>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT AgentId, MAX(CollectedUtc)
            FROM CapabilitySnapshots
            GROUP BY AgentId;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[Guid.Parse(reader.GetString(0))] = reader.IsDBNull(1) ? null : FromDb(reader.GetString(1));
        }

        return result;
    }

    private async Task<IReadOnlyList<AgentCapabilityStateDto>> GetCapabilityStatesAsync(
        SqliteConnection connection,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var capabilities = new List<AgentCapabilityStateDto>();
        var recentSnapshots = await GetRecentCapabilitySnapshotsAsync(connection, agentId, cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                c.CapabilityKey,
                c.DisplayName,
                c.Version,
                c.ActionsJson
            FROM AgentCapabilities c
            WHERE c.AgentId = $agentId
            ORDER BY c.CapabilityKey;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var actions = JsonSerializer.Deserialize<List<CapabilityActionDefinitionDto>>(reader.GetString(3), JsonOptions) ?? [];
            recentSnapshots.TryGetValue(reader.GetString(0), out var snapshotPair);
            var latestSnapshot = snapshotPair is not null && snapshotPair.Count > 0 ? snapshotPair[0] : null;
            var previousSnapshot = snapshotPair is not null && snapshotPair.Count > 1 ? snapshotPair[1] : null;

            capabilities.Add(new AgentCapabilityStateDto(
                new CapabilityDescriptorDto(reader.GetString(0), reader.GetString(1), reader.GetString(2), actions),
                latestSnapshot?.CollectedUtc,
                latestSnapshot?.PayloadJson,
                latestSnapshot?.Hash,
                BuildChangeSummary(reader.GetString(0), latestSnapshot, previousSnapshot)));
        }

        return capabilities;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<CapabilitySnapshotEnvelope>>> GetRecentCapabilitySnapshotsAsync(
        SqliteConnection connection,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<CapabilitySnapshotEnvelope>>(StringComparer.OrdinalIgnoreCase);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SnapshotId, CapabilityKey, CollectedUtc, Hash, PayloadJson
            FROM CapabilitySnapshots
            WHERE AgentId = $agentId
            ORDER BY CapabilityKey, CollectedUtc DESC;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var capabilityKey = reader.GetString(1);
            if (!result.TryGetValue(capabilityKey, out var snapshots))
            {
                snapshots = [];
                result[capabilityKey] = snapshots;
            }

            if (snapshots.Count >= 2)
            {
                continue;
            }

            snapshots.Add(new CapabilitySnapshotEnvelope(
                Guid.Parse(reader.GetString(0)),
                FromDb(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4)));
        }

        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<CapabilitySnapshotEnvelope>)pair.Value, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IReadOnlyList<CapabilitySnapshotHistoryItemDto>> GetCapabilitySnapshotHistoryInternalAsync(
        SqliteConnection connection,
        Guid agentId,
        string capabilityKey,
        int take,
        CancellationToken cancellationToken)
    {
        var snapshots = new List<CapabilitySnapshotEnvelope>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT SnapshotId, CollectedUtc, Hash, PayloadJson
            FROM CapabilitySnapshots
            WHERE AgentId = $agentId
              AND CapabilityKey = $capabilityKey
            ORDER BY CollectedUtc DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$capabilityKey", capabilityKey);
        command.Parameters.AddWithValue("$limit", take + 1);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            snapshots.Add(new CapabilitySnapshotEnvelope(
                Guid.Parse(reader.GetString(0)),
                FromDb(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3)));
        }

        var history = new List<CapabilitySnapshotHistoryItemDto>();
        for (var index = 0; index < Math.Min(take, snapshots.Count); index++)
        {
            var latestSnapshot = snapshots[index];
            var previousSnapshot = index + 1 < snapshots.Count ? snapshots[index + 1] : null;
            history.Add(new CapabilitySnapshotHistoryItemDto(
                latestSnapshot.SnapshotId,
                capabilityKey,
                latestSnapshot.CollectedUtc,
                latestSnapshot.Hash,
                latestSnapshot.PayloadJson,
                BuildChangeSummary(capabilityKey, latestSnapshot, previousSnapshot)));
        }

        return history;
    }

    private async Task<CapabilitySnapshotEnvelope?> GetLatestCapabilitySnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid agentId,
        string capabilityKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT SnapshotId, CollectedUtc, Hash, PayloadJson
            FROM CapabilitySnapshots
            WHERE AgentId = $agentId
              AND CapabilityKey = $capabilityKey
            ORDER BY CollectedUtc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$capabilityKey", capabilityKey);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CapabilitySnapshotEnvelope(
            Guid.Parse(reader.GetString(0)),
            FromDb(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3));
    }

    private static CapabilityChangeSummaryDto BuildChangeSummary(
        string capabilityKey,
        CapabilitySnapshotEnvelope? latestSnapshot,
        CapabilitySnapshotEnvelope? previousSnapshot)
    {
        if (latestSnapshot is null)
        {
            return new CapabilityChangeSummaryDto(false, false, null, "No snapshot captured yet.", []);
        }

        if (previousSnapshot is null)
        {
            return new CapabilityChangeSummaryDto(false, false, null, "First snapshot captured for this capability.", []);
        }

        if (string.Equals(latestSnapshot.Hash, previousSnapshot.Hash, StringComparison.Ordinal))
        {
            return new CapabilityChangeSummaryDto(true, false, previousSnapshot.CollectedUtc, "No changes since the previous snapshot.", []);
        }

        return capabilityKey switch
        {
            CapabilityKeys.Services => BuildServicesChangeSummary(latestSnapshot, previousSnapshot),
            CapabilityKeys.ScheduledTasks => BuildScheduledTasksChangeSummary(latestSnapshot, previousSnapshot),
            CapabilityKeys.Iis => BuildIisChangeSummary(latestSnapshot, previousSnapshot),
            CapabilityKeys.FileTree => BuildFileTreeChangeSummary(latestSnapshot, previousSnapshot),
            CapabilityKeys.UsersAndGroups => BuildUsersAndGroupsChangeSummary(latestSnapshot, previousSnapshot),
            _ => new CapabilityChangeSummaryDto(true, true, previousSnapshot.CollectedUtc, "Snapshot content changed.", [])
        };
    }

    private static CapabilityChangeEventDto? BuildChangeEvent(
        Guid agentId,
        string capabilityKey,
        CapabilitySnapshotEnvelope latestSnapshot,
        CapabilitySnapshotEnvelope? previousSnapshot,
        CapabilityChangeSummaryDto changeSummary)
    {
        if (!changeSummary.HasPreviousSnapshot)
        {
            return new CapabilityChangeEventDto(
                Guid.NewGuid(),
                agentId,
                capabilityKey,
                CapabilityChangeKinds.InitialSnapshot,
                latestSnapshot.CollectedUtc,
                null,
                changeSummary.Summary,
                changeSummary.Highlights);
        }

        if (!changeSummary.HasChanges)
        {
            return null;
        }

        return new CapabilityChangeEventDto(
            Guid.NewGuid(),
            agentId,
            capabilityKey,
            CapabilityChangeKinds.SnapshotChanged,
            latestSnapshot.CollectedUtc,
            previousSnapshot?.CollectedUtc,
            changeSummary.Summary,
            changeSummary.Highlights);
    }

    private static CapabilityChangeSummaryDto BuildServicesChangeSummary(
        CapabilitySnapshotEnvelope latestSnapshot,
        CapabilitySnapshotEnvelope previousSnapshot)
    {
        var latest = JsonSerializer.Deserialize<ServiceSnapshotDto>(latestSnapshot.PayloadJson, JsonOptions) ?? new ServiceSnapshotDto();
        var previous = JsonSerializer.Deserialize<ServiceSnapshotDto>(previousSnapshot.PayloadJson, JsonOptions) ?? new ServiceSnapshotDto();

        var latestMap = latest.Services.ToDictionary(service => service.ServiceName, StringComparer.OrdinalIgnoreCase);
        var previousMap = previous.Services.ToDictionary(service => service.ServiceName, StringComparer.OrdinalIgnoreCase);

        var added = latestMap.Keys.Except(previousMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removed = previousMap.Keys.Except(latestMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var statusChanged = latestMap.Keys.Intersect(previousMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key => !string.Equals(latestMap[key].Status, previousMap[key].Status, StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => key)
            .ToList();

        var highlights = BuildHighlights(
            added, "Added service",
            removed, "Removed service",
            statusChanged.Select(key => $"{key}: {previousMap[key].Status} -> {latestMap[key].Status}").ToList(), "State changed");

        var summary = $"{added.Count} added, {removed.Count} removed, {statusChanged.Count} state changes.";
        return new CapabilityChangeSummaryDto(true, added.Count + removed.Count + statusChanged.Count > 0, previousSnapshot.CollectedUtc, summary, highlights);
    }

    private static CapabilityChangeSummaryDto BuildScheduledTasksChangeSummary(
        CapabilitySnapshotEnvelope latestSnapshot,
        CapabilitySnapshotEnvelope previousSnapshot)
    {
        var latest = JsonSerializer.Deserialize<ScheduledTaskSnapshotDto>(latestSnapshot.PayloadJson, JsonOptions) ?? new ScheduledTaskSnapshotDto();
        var previous = JsonSerializer.Deserialize<ScheduledTaskSnapshotDto>(previousSnapshot.PayloadJson, JsonOptions) ?? new ScheduledTaskSnapshotDto();

        var latestMap = latest.Tasks.ToDictionary(task => $"{task.TaskPath}{task.TaskName}", StringComparer.OrdinalIgnoreCase);
        var previousMap = previous.Tasks.ToDictionary(task => $"{task.TaskPath}{task.TaskName}", StringComparer.OrdinalIgnoreCase);

        var added = latestMap.Keys.Except(previousMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removed = previousMap.Keys.Except(latestMap.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var changed = latestMap.Keys.Intersect(previousMap.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key =>
                !string.Equals(latestMap[key].Status, previousMap[key].Status, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(latestMap[key].TaskToRun, previousMap[key].TaskToRun, StringComparison.OrdinalIgnoreCase))
            .OrderBy(key => key)
            .ToList();

        var changedDetails = changed.Select(key =>
        {
            var previousStatus = previousMap[key].Status;
            var latestStatus = latestMap[key].Status;
            return string.Equals(previousStatus, latestStatus, StringComparison.OrdinalIgnoreCase)
                ? key
                : $"{key}: {previousStatus} -> {latestStatus}";
        }).ToList();

        var highlights = BuildHighlights(
            added, "Added task",
            removed, "Removed task",
            changedDetails, "Changed task");

        var summary = $"{added.Count} added, {removed.Count} removed, {changed.Count} changed.";
        return new CapabilityChangeSummaryDto(true, added.Count + removed.Count + changed.Count > 0, previousSnapshot.CollectedUtc, summary, highlights);
    }

    private static CapabilityChangeSummaryDto BuildIisChangeSummary(
        CapabilitySnapshotEnvelope latestSnapshot,
        CapabilitySnapshotEnvelope previousSnapshot)
    {
        var latest = JsonSerializer.Deserialize<IisSnapshotDto>(latestSnapshot.PayloadJson, JsonOptions) ?? new IisSnapshotDto();
        var previous = JsonSerializer.Deserialize<IisSnapshotDto>(previousSnapshot.PayloadJson, JsonOptions) ?? new IisSnapshotDto();

        var latestPools = latest.AppPools.ToDictionary(pool => pool.Name, StringComparer.OrdinalIgnoreCase);
        var previousPools = previous.AppPools.ToDictionary(pool => pool.Name, StringComparer.OrdinalIgnoreCase);
        var latestSites = latest.Sites.ToDictionary(site => site.Name, StringComparer.OrdinalIgnoreCase);
        var previousSites = previous.Sites.ToDictionary(site => site.Name, StringComparer.OrdinalIgnoreCase);

        var addedPools = latestPools.Keys.Except(previousPools.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removedPools = previousPools.Keys.Except(latestPools.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var changedPools = latestPools.Keys.Intersect(previousPools.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key => !string.Equals(latestPools[key].State, previousPools[key].State, StringComparison.OrdinalIgnoreCase))
            .Select(key => $"{key}: {previousPools[key].State} -> {latestPools[key].State}")
            .OrderBy(x => x)
            .ToList();

        var addedSites = latestSites.Keys.Except(previousSites.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removedSites = previousSites.Keys.Except(latestSites.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var changedSites = latestSites.Keys.Intersect(previousSites.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key =>
                !string.Equals(latestSites[key].State, previousSites[key].State, StringComparison.OrdinalIgnoreCase) ||
                !AreSetsEqual(latestSites[key].Bindings, previousSites[key].Bindings))
            .Select(key => $"{key}: {previousSites[key].State} -> {latestSites[key].State}")
            .OrderBy(x => x)
            .ToList();

        var highlights = new List<string>();
        highlights.AddRange(FormatHighlights("Added app pool", addedPools));
        highlights.AddRange(FormatHighlights("Removed app pool", removedPools));
        highlights.AddRange(FormatHighlights("App pool changed", changedPools));
        highlights.AddRange(FormatHighlights("Added site", addedSites));
        highlights.AddRange(FormatHighlights("Removed site", removedSites));
        highlights.AddRange(FormatHighlights("Site changed", changedSites));

        var changeCount = addedPools.Count + removedPools.Count + changedPools.Count + addedSites.Count + removedSites.Count + changedSites.Count;
        var summary = $"{addedPools.Count + addedSites.Count} added, {removedPools.Count + removedSites.Count} removed, {changedPools.Count + changedSites.Count} changed.";
        return new CapabilityChangeSummaryDto(true, changeCount > 0, previousSnapshot.CollectedUtc, summary, highlights.Take(6).ToList());
    }

    private static CapabilityChangeSummaryDto BuildUsersAndGroupsChangeSummary(
        CapabilitySnapshotEnvelope latestSnapshot,
        CapabilitySnapshotEnvelope previousSnapshot)
    {
        var latest = JsonSerializer.Deserialize<UsersAndGroupsSnapshotDto>(latestSnapshot.PayloadJson, JsonOptions) ?? new UsersAndGroupsSnapshotDto();
        var previous = JsonSerializer.Deserialize<UsersAndGroupsSnapshotDto>(previousSnapshot.PayloadJson, JsonOptions) ?? new UsersAndGroupsSnapshotDto();

        var latestUsers = latest.Users.ToDictionary(user => user.Name, StringComparer.OrdinalIgnoreCase);
        var previousUsers = previous.Users.ToDictionary(user => user.Name, StringComparer.OrdinalIgnoreCase);
        var latestGroups = latest.Groups.ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);
        var previousGroups = previous.Groups.ToDictionary(group => group.Name, StringComparer.OrdinalIgnoreCase);

        var addedUsers = latestUsers.Keys.Except(previousUsers.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removedUsers = previousUsers.Keys.Except(latestUsers.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var changedUsers = latestUsers.Keys.Intersect(previousUsers.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key => latestUsers[key].Enabled != previousUsers[key].Enabled)
            .Select(key => $"{key}: {(previousUsers[key].Enabled ? "Enabled" : "Disabled")} -> {(latestUsers[key].Enabled ? "Enabled" : "Disabled")}")
            .OrderBy(x => x)
            .ToList();

        var addedGroups = latestGroups.Keys.Except(previousGroups.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removedGroups = previousGroups.Keys.Except(latestGroups.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var changedGroups = latestGroups.Keys.Intersect(previousGroups.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key => !AreSetsEqual(
                latestGroups[key].Members.Select(member => member.Name),
                previousGroups[key].Members.Select(member => member.Name)))
            .OrderBy(key => key)
            .ToList();

        var highlights = new List<string>();
        highlights.AddRange(FormatHighlights("Added user", addedUsers));
        highlights.AddRange(FormatHighlights("Removed user", removedUsers));
        highlights.AddRange(FormatHighlights("User changed", changedUsers));
        highlights.AddRange(FormatHighlights("Added group", addedGroups));
        highlights.AddRange(FormatHighlights("Removed group", removedGroups));
        highlights.AddRange(FormatHighlights("Group membership changed", changedGroups));

        var changeCount = addedUsers.Count + removedUsers.Count + changedUsers.Count + addedGroups.Count + removedGroups.Count + changedGroups.Count;
        var summary = $"{addedUsers.Count + addedGroups.Count} added, {removedUsers.Count + removedGroups.Count} removed, {changedUsers.Count + changedGroups.Count} changed.";
        return new CapabilityChangeSummaryDto(true, changeCount > 0, previousSnapshot.CollectedUtc, summary, highlights.Take(6).ToList());
    }

    private static CapabilityChangeSummaryDto BuildFileTreeChangeSummary(
        CapabilitySnapshotEnvelope latestSnapshot,
        CapabilitySnapshotEnvelope previousSnapshot)
    {
        var latest = JsonSerializer.Deserialize<FileTreeSnapshotDto>(latestSnapshot.PayloadJson, JsonOptions) ?? new FileTreeSnapshotDto();
        var previous = JsonSerializer.Deserialize<FileTreeSnapshotDto>(previousSnapshot.PayloadJson, JsonOptions) ?? new FileTreeSnapshotDto();

        var latestRoots = latest.Roots.ToDictionary(root => root.RootPath, StringComparer.OrdinalIgnoreCase);
        var previousRoots = previous.Roots.ToDictionary(root => root.RootPath, StringComparer.OrdinalIgnoreCase);
        var latestNodes = FlattenNodes(latest.Roots);
        var previousNodes = FlattenNodes(previous.Roots);

        var addedRoots = latestRoots.Keys.Except(previousRoots.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removedRoots = previousRoots.Keys.Except(latestRoots.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var addedNodes = latestNodes.Keys.Except(previousNodes.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var removedNodes = previousNodes.Keys.Except(latestNodes.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList();
        var changedNodes = latestNodes.Keys.Intersect(previousNodes.Keys, StringComparer.OrdinalIgnoreCase)
            .Where(key =>
                latestNodes[key].IsDirectory != previousNodes[key].IsDirectory ||
                latestNodes[key].SizeBytes != previousNodes[key].SizeBytes ||
                !string.Equals(latestNodes[key].Owner, previousNodes[key].Owner, StringComparison.OrdinalIgnoreCase) ||
                !AreSetsEqual(latestNodes[key].Permissions, previousNodes[key].Permissions))
            .OrderBy(key => key)
            .ToList();

        var highlights = new List<string>();
        highlights.AddRange(FormatHighlights("Added root", addedRoots));
        highlights.AddRange(FormatHighlights("Removed root", removedRoots));
        highlights.AddRange(FormatHighlights("Added path", addedNodes));
        highlights.AddRange(FormatHighlights("Removed path", removedNodes));
        highlights.AddRange(FormatHighlights("Changed path", changedNodes));

        var changeCount = addedRoots.Count + removedRoots.Count + addedNodes.Count + removedNodes.Count + changedNodes.Count;
        var summary = $"{addedNodes.Count + addedRoots.Count} added, {removedNodes.Count + removedRoots.Count} removed, {changedNodes.Count} changed.";
        return new CapabilityChangeSummaryDto(true, changeCount > 0, previousSnapshot.CollectedUtc, summary, highlights.Take(6).ToList());
    }

    private static Dictionary<string, FileTreeNodeDto> FlattenNodes(IReadOnlyList<FileTreeRootDto> roots)
    {
        var nodes = new Dictionary<string, FileTreeNodeDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            foreach (var node in root.Nodes)
            {
                FlattenNode(nodes, node);
            }
        }

        return nodes;
    }

    private static void FlattenNode(IDictionary<string, FileTreeNodeDto> nodes, FileTreeNodeDto node)
    {
        nodes[node.FullPath] = node;
        foreach (var child in node.Children)
        {
            FlattenNode(nodes, child);
        }
    }

    private static bool AreSetsEqual(IEnumerable<string>? left, IEnumerable<string>? right)
        => new HashSet<string>(left ?? [], StringComparer.OrdinalIgnoreCase).SetEquals(right ?? []);

    private static List<string> BuildHighlights(
        IReadOnlyList<string> added,
        string addedPrefix,
        IReadOnlyList<string> removed,
        string removedPrefix,
        IReadOnlyList<string> changed,
        string changedPrefix)
    {
        var highlights = new List<string>();
        highlights.AddRange(FormatHighlights(addedPrefix, added));
        highlights.AddRange(FormatHighlights(removedPrefix, removed));
        highlights.AddRange(FormatHighlights(changedPrefix, changed));
        return highlights.Take(6).ToList();
    }

    private static IEnumerable<string> FormatHighlights(string prefix, IReadOnlyList<string> items)
        => items.Take(2).Select(item => $"{prefix}: {item}");

    private async Task InsertChangeEventAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CapabilitySnapshotEnvelope latestSnapshot,
        CapabilitySnapshotEnvelope? previousSnapshot,
        CapabilityChangeEventDto changeEvent,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO CapabilityChangeEvents (
                ChangeEventId, AgentId, CapabilityKey, SnapshotId, PreviousSnapshotId, ChangeKind, CollectedUtc, PreviousCollectedUtc, Summary, HighlightsJson
            ) VALUES (
                $changeEventId, $agentId, $capabilityKey, $snapshotId, $previousSnapshotId, $changeKind, $collectedUtc, $previousCollectedUtc, $summary, $highlightsJson
            );
            """;
        command.Parameters.AddWithValue("$changeEventId", changeEvent.ChangeEventId.ToString("D"));
        command.Parameters.AddWithValue("$agentId", changeEvent.AgentId.ToString("D"));
        command.Parameters.AddWithValue("$capabilityKey", changeEvent.CapabilityKey);
        command.Parameters.AddWithValue("$snapshotId", latestSnapshot.SnapshotId.ToString("D"));
        command.Parameters.AddWithValue("$previousSnapshotId", previousSnapshot is null ? DBNull.Value : previousSnapshot.SnapshotId.ToString("D"));
        command.Parameters.AddWithValue("$changeKind", changeEvent.ChangeKind);
        command.Parameters.AddWithValue("$collectedUtc", ToDb(changeEvent.CollectedUtc));
        command.Parameters.AddWithValue("$previousCollectedUtc", changeEvent.PreviousCollectedUtc is null ? DBNull.Value : ToDb(changeEvent.PreviousCollectedUtc.Value));
        command.Parameters.AddWithValue("$summary", changeEvent.Summary);
        command.Parameters.AddWithValue("$highlightsJson", JsonSerializer.Serialize(changeEvent.Highlights, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<CapabilityChangeEventDto>> GetRecentChangeEventsAsync(
        SqliteConnection connection,
        Guid? agentId,
        int take,
        CancellationToken cancellationToken)
    {
        var changes = new List<CapabilityChangeEventDto>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            agentId is null
                ? """
                  SELECT ChangeEventId, AgentId, CapabilityKey, ChangeKind, CollectedUtc, PreviousCollectedUtc, Summary, HighlightsJson
                  FROM CapabilityChangeEvents
                  ORDER BY CollectedUtc DESC
                  LIMIT $take;
                  """
                : """
                  SELECT ChangeEventId, AgentId, CapabilityKey, ChangeKind, CollectedUtc, PreviousCollectedUtc, Summary, HighlightsJson
                  FROM CapabilityChangeEvents
                  WHERE AgentId = $agentId
                  ORDER BY CollectedUtc DESC
                  LIMIT $take;
                  """;
        command.Parameters.AddWithValue("$take", take);
        if (agentId is not null)
        {
            command.Parameters.AddWithValue("$agentId", agentId.Value.ToString("D"));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            changes.Add(new CapabilityChangeEventDto(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                FromDb(reader.GetString(4)),
                reader.IsDBNull(5) ? null : FromDb(reader.GetString(5)),
                reader.GetString(6),
                JsonSerializer.Deserialize<List<string>>(reader.GetString(7), JsonOptions) ?? []));
        }

        return changes;
    }

    private async Task<IReadOnlyList<ActionCommandSummaryDto>> GetRecentActionsAsync(
        SqliteConnection connection,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var actions = new List<ActionCommandSummaryDto>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ActionId, AgentId, CapabilityKey, ActionKey, TargetKey, Status, RequestedBy, CreatedUtc, LeasedUtc, LeaseExpiresUtc, CompletedUtc, AttemptCount, ResultMessage, ErrorMessage
            FROM ActionCommands
            WHERE AgentId = $agentId
            ORDER BY CreatedUtc DESC
            LIMIT 20;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            actions.Add(new ActionCommandSummaryDto(
                Guid.Parse(reader.GetString(0)),
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                FromDb(reader.GetString(7)),
                reader.IsDBNull(8) ? null : FromDb(reader.GetString(8)),
                reader.IsDBNull(9) ? null : FromDb(reader.GetString(9)),
                reader.IsDBNull(10) ? null : FromDb(reader.GetString(10)),
                reader.IsDBNull(11) ? 0 : Convert.ToInt32(reader.GetValue(11)),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13)));
        }

        return actions;
    }

    private async Task<ActionCommandSummaryDto?> GetActionSummaryByIdAsync(
        SqliteConnection connection,
        Guid actionId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ActionId, AgentId, CapabilityKey, ActionKey, TargetKey, Status, RequestedBy, CreatedUtc, LeasedUtc, LeaseExpiresUtc, CompletedUtc, AttemptCount, ResultMessage, ErrorMessage
            FROM ActionCommands
            WHERE ActionId = $actionId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$actionId", actionId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ActionCommandSummaryDto(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            FromDb(reader.GetString(7)),
            reader.IsDBNull(8) ? null : FromDb(reader.GetString(8)),
            reader.IsDBNull(9) ? null : FromDb(reader.GetString(9)),
            reader.IsDBNull(10) ? null : FromDb(reader.GetString(10)),
            reader.IsDBNull(11) ? 0 : Convert.ToInt32(reader.GetValue(11)),
            reader.IsDBNull(12) ? null : reader.GetString(12),
            reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private static StoredAgent ReadStoredAgent(SqliteDataReader reader)
        => new(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            FromDb(reader.GetString(7)),
            reader.IsDBNull(8) ? null : FromDb(reader.GetString(8)),
            reader.IsDBNull(9) ? null : FromDb(reader.GetString(9)));

    private static void BindAgentParameters(SqliteCommand command, StoredAgent agent)
    {
        command.Parameters.AddWithValue("$agentId", agent.AgentId.ToString("D"));
        command.Parameters.AddWithValue("$installationId", agent.InstallationId);
        command.Parameters.AddWithValue("$displayName", agent.DisplayName);
        command.Parameters.AddWithValue("$machineName", agent.MachineName);
        command.Parameters.AddWithValue("$status", agent.Status);
        command.Parameters.AddWithValue("$agentVersion", agent.AgentVersion);
        command.Parameters.AddWithValue("$accessToken", agent.AccessToken);
        command.Parameters.AddWithValue("$createdUtc", ToDb(agent.CreatedUtc));
        command.Parameters.AddWithValue("$approvedUtc", agent.ApprovedUtc is null ? DBNull.Value : ToDb(agent.ApprovedUtc.Value));
        command.Parameters.AddWithValue("$lastSeenUtc", agent.LastSeenUtc is null ? DBNull.Value : ToDb(agent.LastSeenUtc.Value));
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string tableName,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var inspectCommand = connection.CreateCommand();
        inspectCommand.CommandText = $"PRAGMA table_info({tableName});";

        await using var reader = await inspectCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        await using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {definition};";
        await alterCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ToDb(DateTimeOffset value) => value.ToString("O");

    private static DateTimeOffset FromDb(string value) => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private sealed record ActionQueueStats(
        int PendingCount,
        int InProgressCount,
        int FailedCount);

    private sealed record CapabilitySnapshotEnvelope(
        Guid SnapshotId,
        DateTimeOffset CollectedUtc,
        string Hash,
        string PayloadJson);
}
