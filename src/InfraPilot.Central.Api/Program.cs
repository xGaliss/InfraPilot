using InfraPilot.Central.Application;
using InfraPilot.Central.Infrastructure.Sqlite;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Agents;
using InfraPilot.Contracts.Capabilities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfraPilotCentralApplication(builder.Configuration);
builder.Services.AddInfraPilotSqliteStore();

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
});

app.MapGet("/api/agents", async (CentralService centralService, CancellationToken cancellationToken)
    => Results.Ok(await centralService.GetAgentsAsync(cancellationToken)));

app.MapGet("/api/agents/{agentId:guid}", async (Guid agentId, CentralService centralService, CancellationToken cancellationToken) =>
{
    var detail = await centralService.GetAgentDetailAsync(agentId, cancellationToken);
    return detail is null ? Results.NotFound() : Results.Ok(detail);
});

app.MapPost("/api/agents/{agentId:guid}/approve", async (Guid agentId, CentralService centralService, CancellationToken cancellationToken) =>
{
    var approved = await centralService.ApproveAgentAsync(agentId, cancellationToken);
    return approved ? Results.Ok(new { approved = true }) : Results.NotFound();
});

app.MapPost("/api/actions", async (
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

app.Run();
