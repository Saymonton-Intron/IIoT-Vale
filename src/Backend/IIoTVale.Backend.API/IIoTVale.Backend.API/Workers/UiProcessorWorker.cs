using IIoTVale.Backend.API.Services;
using IIoTVale.Backend.API.Wrappers;
using IIoTVale.Backend.Core.DTOs;
using System.Diagnostics;
using System.Threading.Channels;

namespace IIoTVale.Backend.API.Workers
{
    public class UiProcessorWorker : BackgroundService
    {
        private readonly ILogger<UiProcessorWorker> _logger;
        private readonly ChannelReader<ITelemetryDto> _uiReader;
        private readonly WebSocketHandler _webSocket;

        public UiProcessorWorker(ILogger<UiProcessorWorker> logger, UiChannel uiChannel, WebSocketHandler webSocket)
        {
            _logger = logger;
            _uiReader = uiChannel.Reader;
            _webSocket = webSocket;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (ITelemetryDto dto in _uiReader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (dto is DataStreamingDto data)
                    {
                        var sensorMac = data.SensorMAC;
                        await _webSocket.Broadcast(new
                        {
                            sensor = data.SensorMAC,
                            frequency = data.Frequency,
                            // Transformamos a lista DataModel em uma nova lista de objetos anônimos
                            data = data.DataModel.Select(m => new
                            {
                                ts = m.SampleTime,
                                vals = new
                                {
                                    X = m.AccX,
                                    Y = m.AccY,
                                    Z = m.AccZ
                                }
                            }).ToList()
                        });
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
