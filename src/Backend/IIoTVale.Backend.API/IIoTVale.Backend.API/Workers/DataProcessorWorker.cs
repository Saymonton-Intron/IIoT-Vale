
using IIoTVale.Backend.API.Services;
using IIoTVale.Backend.Core.DTOs;
using System.Threading.Channels;

namespace IIoTVale.Backend.API.Workers
{
    public class DataProcessorWorker : BackgroundService
    {
        private readonly ChannelReader<ITelemetryDto> _reader;

        private readonly List<DataStreamingDto> _batchBuffer = [];
        private readonly int BATCH_SIZE = 5;
        private readonly ILogger<DataProcessorWorker> _logger;
        private readonly DatabaseService _databaseService;

        public DataProcessorWorker(ILogger<DataProcessorWorker> _logger, Channel<ITelemetryDto> _channel, DatabaseService databaseService)
        {
            this._logger = _logger;
            this._databaseService = databaseService;
            this._reader = _channel.Reader;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (ITelemetryDto dto in _reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (dto is DataStreamingDto data)
                    {
                        // Fazer um downSampling e enviar para o front async
                        // ...

                        _batchBuffer.Add(data);

                        if (_batchBuffer.Count >= BATCH_SIZE)
                        {
                            await _databaseService.SaveBatchAsync(_batchBuffer, stoppingToken);
                            _batchBuffer.Clear();
                        }

                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while processing data.");
                }
            }
        }
    }
}
