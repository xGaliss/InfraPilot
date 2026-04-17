namespace InfraPilot.Agent.Core;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public abstract class AgentTaskHostedService : BackgroundService
{
    private readonly ILogger _logger;

    protected AgentTaskHostedService(ILogger logger)
    {
        _logger = logger;
    }

    protected abstract TimeSpan Interval { get; }

    protected virtual bool RunOnStartup => true;

    protected abstract Task ExecuteIterationAsync(CancellationToken stoppingToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (RunOnStartup)
        {
            await SafeExecuteAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(Interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeExecuteAsync(stoppingToken);
        }
    }

    private async Task SafeExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ExecuteIterationAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Agent background task {TaskName} failed.", GetType().Name);
        }
    }
}
