namespace InfraPilot.Central.Infrastructure.Sqlite;

using System.Text.Json;
using InfraPilot.Central.Application;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.Common;
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
                ResultMessage TEXT NULL,
                ErrorMessage TEXT NULL,
                OutputJson TEXT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_CapabilitySnapshots_Agent_Capability_CollectedUtc
                ON CapabilitySnapshots (AgentId, CapabilityKey, CollectedUtc DESC);

            CREATE INDEX IF NOT EXISTS IX_ActionCommands_Agent_Status_CreatedUtc
                ON ActionCommands (AgentId, Status, CreatedUtc);
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
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
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO CapabilitySnapshots (SnapshotId, AgentId, CapabilityKey, CollectedUtc, Hash, PayloadJson)
                VALUES ($snapshotId, $agentId, $capabilityKey, $collectedUtc, $hash, $payloadJson);
                """;
            command.Parameters.AddWithValue("$snapshotId", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
            command.Parameters.AddWithValue("$capabilityKey", snapshot.CapabilityKey);
            command.Parameters.AddWithValue("$collectedUtc", ToDb(collectedUtc));
            command.Parameters.AddWithValue("$hash", snapshot.Hash);
            command.Parameters.AddWithValue("$payloadJson", snapshot.PayloadJson);
            await command.ExecuteNonQueryAsync(cancellationToken);
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
                    LeaseExpiresUtc = $leaseExpiresUtc
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
              AND Status = $inProgress;
            """;
        command.Parameters.AddWithValue("$status", result.Status);
        command.Parameters.AddWithValue("$completedUtc", ToDb(result.CompletedUtc));
        command.Parameters.AddWithValue("$resultMessage", (object?)result.ResultMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)result.ErrorMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$outputJson", (object?)result.OutputJson ?? DBNull.Value);
        command.Parameters.AddWithValue("$actionId", actionId.ToString("D"));
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));
        command.Parameters.AddWithValue("$inProgress", ActionStatuses.InProgress);
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
            recentActions);
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
                ActionId, AgentId, CapabilityKey, ActionKey, TargetKey, PayloadJson, Status, RequestedBy, CreatedUtc
            ) VALUES (
                $actionId, $agentId, $capabilityKey, $actionKey, $targetKey, $payloadJson, $status, $requestedBy, $createdUtc
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
            null,
            null);
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
            SELECT ActionId, AgentId, CapabilityKey, ActionKey, TargetKey, Status, RequestedBy, CreatedUtc, LeasedUtc, LeaseExpiresUtc, CompletedUtc, ResultMessage, ErrorMessage
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
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12));
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

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                c.CapabilityKey,
                c.DisplayName,
                c.Version,
                c.ActionsJson,
                s.CollectedUtc,
                s.PayloadJson,
                s.Hash
            FROM AgentCapabilities c
            LEFT JOIN CapabilitySnapshots s
                ON s.AgentId = c.AgentId
               AND s.CapabilityKey = c.CapabilityKey
               AND s.CollectedUtc = (
                    SELECT MAX(CollectedUtc)
                    FROM CapabilitySnapshots
                    WHERE AgentId = c.AgentId
                      AND CapabilityKey = c.CapabilityKey
               )
            WHERE c.AgentId = $agentId
            ORDER BY c.CapabilityKey;
            """;
        command.Parameters.AddWithValue("$agentId", agentId.ToString("D"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var actions = JsonSerializer.Deserialize<List<CapabilityActionDefinitionDto>>(reader.GetString(3), JsonOptions) ?? [];
            capabilities.Add(new AgentCapabilityStateDto(
                new CapabilityDescriptorDto(reader.GetString(0), reader.GetString(1), reader.GetString(2), actions),
                reader.IsDBNull(4) ? null : FromDb(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return capabilities;
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
            SELECT ActionId, AgentId, CapabilityKey, ActionKey, TargetKey, Status, RequestedBy, CreatedUtc, LeasedUtc, LeaseExpiresUtc, CompletedUtc, ResultMessage, ErrorMessage
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
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return actions;
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

    private static string ToDb(DateTimeOffset value) => value.ToString("O");

    private static DateTimeOffset FromDb(string value) => DateTimeOffset.Parse(value, null, System.Globalization.DateTimeStyles.RoundtripKind);

    private sealed record ActionQueueStats(
        int PendingCount,
        int InProgressCount,
        int FailedCount);
}
