using IIoTVale.Backend.API.Services;
using IIoTVale.Backend.API.Wrappers;
using IIoTVale.Backend.Core.DTOs;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net.Mail;
using System.Threading;
using System.Threading.Channels;

namespace IIoTVale.Backend.API.Workers
{
    public enum RequestedUI
    {
        NONE,
        TIME_DOMAIN,
        FFT,
    }

    public class UiProcessorWorker : BackgroundService
    {
        private readonly ILogger<UiProcessorWorker> _logger;
        private readonly ChannelReader<ITelemetryDto> _uiReader;
        private readonly Queue<DataModel> _fftQueue = new();
        private const int _maxSizeQueue = 1000;
        private readonly WebSocketHandler _webSocket;
        private RequestedUI _currentRequestedUI = RequestedUI.FFT;

        public UiProcessorWorker(ILogger<UiProcessorWorker> logger, UiChannel uiChannel, WebSocketHandler webSocket)
        {
            _logger = logger;
            _uiReader = uiChannel.Reader;
            _webSocket = webSocket;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using CancellationTokenSource cts = new();
                using CancellationTokenSource switchCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, cts.Token);

                switch (_currentRequestedUI)
                {
                    case RequestedUI.NONE: await Task.Delay(100, stoppingToken); break;
                    case RequestedUI.TIME_DOMAIN: await SendStreamingData(switchCts.Token); break;
                    case RequestedUI.FFT: await SendFFTData(switchCts.Token); break;
                }
            }
        }
        private async Task SendStreamingData(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (ITelemetryDto dto in _uiReader.ReadAllAsync(cancellationToken))
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
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trying process data to UI.");
            }
        }
        private async Task SendFFTData(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (ITelemetryDto dto in _uiReader.ReadAllAsync(cancellationToken))
                {
                    if (dto is DataStreamingDto data)
                    {
                        _fftQueue.AddToQueue([.. data.DataModel], _maxSizeQueue);
                        if (_fftQueue.Count < _maxSizeQueue) continue;

                        var sensorMac = data.SensorMAC;

                        double[] bufferX = [.. _fftQueue.Select(d => d.AccX)];
                        double[] bufferY = [.. _fftQueue.Select(d => d.AccY)];
                        double[] bufferZ = [.. _fftQueue.Select(d => d.AccZ)];

                        List<SignalProcessingService.FrequencyBin> fftResultX = SignalProcessingService.ComputeFft(bufferX, data.Frequency);
                        List<SignalProcessingService.FrequencyBin> fftResultY = SignalProcessingService.ComputeFft(bufferY, data.Frequency);
                        List<SignalProcessingService.FrequencyBin> fftResultZ = SignalProcessingService.ComputeFft(bufferZ, data.Frequency);

                        await _webSocket.Broadcast(new
                        {
                            Type = "fft_data",
                            Sensor = sensorMac,
                            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            Data = new
                            {
                                // Extrai apenas as frequências do resultado X (eixo comum)
                                Frequencies = fftResultX.Select(bin => bin.Frequency).ToList(),

                                // Extrai magnitudes
                                X = fftResultX.Select(bin => bin.Magnitude).ToList(),
                                Y = fftResultY.Select(bin => bin.Magnitude).ToList(),
                                Z = fftResultZ.Select(bin => bin.Magnitude).ToList()
                            }
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _fftQueue.Clear();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error trying process data to UI.");
            }
        }

    }
    public static class UiProcessorExtensions
    {
        public static void AddToQueue<T>(this Queue<T> queue, T newValue, int MaxSize = int.MaxValue)
        {
            if (queue.Count == MaxSize)
            {
                queue.Dequeue();
            }
            queue.Enqueue(newValue);
        }
        public static void AddToQueue<T>(this Queue<T> queue, T[] newValue, int MaxSize = int.MaxValue)
        {
            // Garante que só vai ter 1000
            if (newValue.Length > MaxSize)
                newValue = [.. newValue.TakeLast(MaxSize)];

            // Abrir espaço se necessario
            // Pegar quanto ainda tem
            var sobrando = MaxSize - queue.Count;

            // Verificar quanto que precisa tirar da queue
            var precisaDe = newValue.Length - sobrando;

            for (int i = 0; i < precisaDe; i++)
            {
                queue.Dequeue();
            }

            // adicionar
            if (sobrando >= newValue.Length)
            {
                foreach (var value in newValue)
                {
                    queue.Enqueue(value);
                }
            }
        }
    }
}
