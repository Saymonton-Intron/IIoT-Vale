
using IIoTVale.Backend.API.Services;
using IIoTVale.Backend.API.Wrappers;
using IIoTVale.Backend.Core.DTOs.Telemetry;
using System.Threading.Channels;

namespace IIoTVale.Backend.API.Workers
{
    public class DbProcessorWorker : BackgroundService
    {
        private readonly ChannelReader<ITelemetryDto> _dbReader;

        //Lembrar de se inscrever no UPDATE do sensor pra limpar esse camarada qunado o sensor parar de enviar dados,
        // pra quando for batchear não enviar dado antigo (não critico mas boa prática).
        private readonly List<DataStreamingDto> _batchBuffer = [];
        private readonly int BATCH_SIZE = 5;
        private readonly ILogger<DbProcessorWorker> _logger;
        private readonly DatabaseService _databaseService;

        public DbProcessorWorker(ILogger<DbProcessorWorker> _logger, DbChannel dbChannel, DatabaseService databaseService)
        {
            this._logger = _logger;
            this._databaseService = databaseService;
            this._dbReader = dbChannel.Reader;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await foreach (ITelemetryDto dto in _dbReader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    if (dto is DataStreamingDto data)
                    {
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
