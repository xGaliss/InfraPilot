using InfraPilot.Central.Application;
using InfraPilot.Central.Api;
using InfraPilot.Central.Infrastructure.Sqlite;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfraPilotCentralApplication(builder.Configuration);
builder.Services.AddInfraPilotSqliteStore();
builder.Services.AddHostedService<RetentionCleanupHostedService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var centralService = scope.ServiceProvider.GetRequiredService<CentralService>();
    await centralService.InitializeAsync(CancellationToken.None);
}

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "InfraPilot.Central.Api",
    utc = DateTimeOffset.UtcNow
}));

app.MapPost("/api/agents/enroll", async (
    HttpRequest request,
    AgentEnrollRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var response = await centralService.EnrollAsync(
            request.Headers["x-enrollment-key"].FirstOrDefault(),
            payload,
            cancellationToken);

        return Results.Ok(response);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
});

app.MapPost("/api/agents/heartbeat", async (
    HttpRequest request,
    AgentHeartbeatRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        await centralService.RecordHeartbeatAsync(
            payload.InstallationId,
            request.Headers["x-agent-token"].FirstOrDefault(),
            payload,
            cancellationToken);

        return Results.Ok();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
});

app.MapPost("/api/agents/capabilities", async (
    HttpRequest request,
    AgentCapabilityPublishRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        await centralService.PublishCapabilitiesAsync(
            payload.InstallationId,
            request.Headers["x-agent-token"].FirstOrDefault(),
            payload.Capabilities,
            cancellationToken);

        return Results.Ok();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
});

app.MapPost("/api/agents/snapshots", async (
    HttpRequest request,
    AgentSnapshotPublishRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        await centralService.PublishSnapshotsAsync(
            payload.InstallationId,
            request.Headers["x-agent-token"].FirstOrDefault(),
            payload.CollectedUtc,
            payload.Snapshots,
            cancellationToken);

        return Results.Ok();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
});

app.MapPost("/api/agents/actions/pull", async (
    HttpRequest request,
    AgentPullRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var command = await centralService.PullNextActionAsync(
            payload.InstallationId,
            request.Headers["x-agent-token"].FirstOrDefault(),
            cancellationToken);

        return command is null ? Results.NoContent() : Results.Ok(command);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (InvalidOperationException)
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }
});

app.MapPost("/api/actions/{actionId:guid}/result", async (
    Guid actionId,
    HttpRequest request,
    AgentActionResultReportDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        await centralService.ReportActionResultAsync(
            actionId,
            payload.InstallationId,
            request.Headers["x-agent-token"].FirstOrDefault(),
            payload,
            cancellationToken);

        return Results.Ok();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (InvalidOperationException exception)
    {
        return Results.Conflict(new { error = exception.Message });
    }
});

var operatorGroup = app.MapGroup("/api")
    .AddEndpointFilter<OperatorApiKeyEndpointFilter>();

operatorGroup.MapGet("/agents", async (CentralService centralService, CancellationToken cancellationToken)
    => Results.Ok(await centralService.GetAgentsAsync(cancellationToken)));

operatorGroup.MapGet("/agents/{agentId:guid}", async (Guid agentId, CentralService centralService, CancellationToken cancellationToken) =>
{
    var detail = await centralService.GetAgentDetailAsync(agentId, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

operatorGroup.MapGet("/agents/{agentId:guid}/capabilities/{capabilityKey}/history", async (
    Guid agentId,
    string capabilityKey,
    int? take,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    var history = await centralService.GetCapabilityHistoryAsync(agentId, capabilityKey, take ?? 25, cancellationToken);
    return Results.Ok(history);
});

operatorGroup.MapGet("/agents/{agentId:guid}/changes", async (
    Guid agentId,
    int? take,
    CentralService centralService,
    CancellationToken cancellationToken) =>
    Results.Ok(await centralService.GetChangeFeedAsync(agentId, take ?? 20, cancellationToken)));

operatorGroup.MapGet("/changes", async (
    int? take,
    CentralService centralService,
    CancellationToken cancellationToken) =>
    Results.Ok(await centralService.GetChangeFeedAsync(null, take ?? 50, cancellationToken)));

operatorGroup.MapPost("/agents/{agentId:guid}/approve", async (
    Guid agentId,
    AgentControlRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await centralService.ApproveAgentAsync(agentId, payload, cancellationToken));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

operatorGroup.MapPost("/agents/{agentId:guid}/revoke", async (
    Guid agentId,
    AgentControlRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await centralService.RevokeAgentAsync(agentId, payload, cancellationToken));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

operatorGroup.MapPost("/agents/{agentId:guid}/reset-token", async (
    Guid agentId,
    AgentControlRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await centralService.ResetAgentTokenAsync(agentId, payload, cancellationToken));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

operatorGroup.MapPost("/actions", async (
    ActionCommandCreateRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var action = await centralService.CreateActionAsync(payload, cancellationToken);
        return Results.Ok(action);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

operatorGroup.MapPost("/actions/{actionId:guid}/cancel", async (
    Guid actionId,
    ActionCommandCancelRequestDto payload,
    CentralService centralService,
    CancellationToken cancellationToken) =>
{
    try
    {
        var action = await centralService.CancelPendingActionAsync(actionId, payload, cancellationToken);
        return Results.Ok(action);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.Run();
