using InfraPilot.Central.Application;
using Microsoft.Extensions.Options;

namespace InfraPilot.Central.Api;

public sealed class RetentionCleanupHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly CentralOptions _options;
    private readonly ILogger<RetentionCleanupHostedService> _logger;

    public RetentionCleanupHostedService(
        IServiceProvider serviceProvider,
        IOptions<CentralOptions> options,
        ILogger<RetentionCleanupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanupAsync(stoppingToken);

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(5, _options.CleanupIntervalMinutes)));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCleanupAsync(stoppingToken);
        }
    }

    private async Task RunCleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var centralService = scope.ServiceProvider.GetRequiredService<CentralService>();
            var result = await centralService.CleanupExpiredDataAsync(cancellationToken);

            if (result.SnapshotsDeleted == 0 && result.ChangeEventsDeleted == 0 && result.ActionsDeleted == 0)
            {
                return;
            }

            _logger.LogInformation(
                "Retention cleanup removed Snapshots={SnapshotsDeleted} ChangeEvents={ChangeEventsDeleted} Actions={ActionsDeleted}",
                result.SnapshotsDeleted,
                result.ChangeEventsDeleted,
                result.ActionsDeleted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Retention cleanup failed.");
        }
    }
}
