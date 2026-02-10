
using IIoTVale.Backend.Core.DTOs;
using System.Threading.Channels;

namespace IIoTVale.Backend.API.Workers
{
    public class UiProcessorWorker : BackgroundService
    {
        private readonly ILogger<UiProcessorWorker> _logger;
        private readonly ChannelReader<ITelemetryDto> _uiReader;

        public UiProcessorWorker(ILogger<UiProcessorWorker> logger, ChannelReader<ITelemetryDto> uiReader)
        {
            _logger = logger;
            _uiReader = uiReader;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (ITelemetryDto dto in _uiReader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (dto is DataStreamingDto data)
                    {
                        //Enviar via signalR
                        await Task.Delay(100, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error trying process data to UI.");
                }
            }
        }
    }
}
