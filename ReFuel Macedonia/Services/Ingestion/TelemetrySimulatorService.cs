using Microsoft.Extensions.Options;
using ReFuel_Macedonia.Infrastructure;

namespace ReFuel_Macedonia.Services.Ingestion;

public class TelemetrySimulatorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ReFuelOptions _options;
    private readonly ILogger<TelemetrySimulatorService> _logger;

    public TelemetrySimulatorService(
        IServiceScopeFactory scopes,
        IOptions<ReFuelOptions> options,
        ILogger<TelemetrySimulatorService> logger)
    {
        _scopes = scopes;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableTelemetrySimulator)
        {
            _logger.LogInformation("The tanker telemetry simulator is switched off. Real devices can post to /api/iot/tankers/{{id}}/telemetry.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.TelemetryIntervalSeconds, 15, 600));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var ticks = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                var fleet = scope.ServiceProvider.GetRequiredService<IFleetService>();

                var recorded = await fleet.SimulateTickAsync();
                ticks++;

                if (ticks % 10 == 1)
                    _logger.LogInformation("Telemetry simulator recorded {Count} tanker position(s).", recorded);

                if (ticks % 12 == 0)
                {
                    var alerts = await fleet.DetectAnomaliesAsync();
                    if (alerts > 0) _logger.LogInformation("Raised {Count} new fuel alert(s).", alerts);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "A telemetry simulator tick failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
