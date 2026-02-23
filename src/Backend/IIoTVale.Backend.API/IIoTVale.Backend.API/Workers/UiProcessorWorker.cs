using IIoTVale.Backend.API.Services;
using IIoTVale.Backend.API.Wrappers;
using IIoTVale.Backend.Core.DTOs.Telemetry;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Net.Mail;
using System.Threading;
using System.Threading.Channels;
using static IIoTVale.Backend.API.Services.WebSocketClient;

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
        private readonly ConcurrentDictionary<string, DateTime> _activeSensors = new();
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
                //  Processar dados para AMBOS os modos simultaneamente
                // Cada cliente receberá apenas o que solicitou

                var tasks = new List<Task>
                {
                    SendStreamingAndHeartbeatToSubscribers(stoppingToken),
                    SendFFTDataToSubscribers(stoppingToken),
                    SendAvailableSensorsList(stoppingToken)
                };

                await Task.WhenAny(tasks);
            }
        }

        private async Task SendAvailableSensorsList(CancellationToken stoppingToken)
        {
            try
            {
                TimeSpan _maxIdleSeconds = TimeSpan.FromSeconds(10);
                while (!stoppingToken.IsCancellationRequested)
                {
                    var now = DateTime.Now;
                    var activeSensorsList = _activeSensors
                        .Where(kvp => now - kvp.Value < _maxIdleSeconds)
                        .Select(kvp => new { mac = kvp.Key, lastSeen = kvp.Value })
                        .ToList();

                    // Remover sensores que não enviaram dados recentemente
                    foreach (var inactiveSensor in _activeSensors
                        .Where(kvp => now - kvp.Value >= _maxIdleSeconds)
                        .Select(kvp => kvp.Key)
                        .ToList())
                    {
                        _activeSensors.TryRemove(inactiveSensor, out _);
                    }

                    var payload = new
                    {
                        type = "available_sensors",
                        connectedSensors = activeSensorsList,
                        timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                    // Enviar para TODOS os clientes conectados
                    await _webSocket.Broadcast(payload);

                    // Enviar a cada 2 segundos
                    await Task.Delay(2000, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending available sensors list.");
            }
        }

        private async Task SendStreamingAndHeartbeatToSubscribers(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (ITelemetryDto dto in _uiReader.ReadAllAsync(cancellationToken))
                {
                    if (dto is DataStreamingDto streaming)
                    {
                        _activeSensors.AddOrUpdate(streaming.SensorMAC, DateTime.Now, (_, _) => DateTime.Now);
                        var payload = new
                        {
                            type = "time_domain",
                            sensor = streaming.SensorMAC,
                            frequency = streaming.Frequency,
                            data = streaming.DataModel.Select(m => new
                            {
                                ts = m.SampleTime,
                                vals = new { X = m.AccX, Y = m.AccY, Z = m.AccZ }
                            }).ToList()
                        };

                        //  Enviar apenas para clientes que solicitaram TIME_DOMAIN
                        await _webSocket.SendToFilteredClients(payload,
                            client => client.RequestedUI == RequestedUI.TIME_DOMAIN && client.RequestedSensorMac == streaming.SensorMAC);
                    }
                    else if (dto is HeartbeatTelemetryDto heartbeat)
                    {
                        _activeSensors.AddOrUpdate(heartbeat.SensorMAC, DateTime.Now, (_, _) => DateTime.Now);
                        var payload = new
                        {
                            type = "heartbeat",
                            sensor = heartbeat.SensorMAC,
                            lqi = heartbeat.NetworkStatus.LQI,
                            temperature = heartbeat.NetworkStatus.InternalTemperature,
                            dataLogger = heartbeat.DataLoggerMemoryPercentage,
                            batteryVolts = heartbeat.BatteryVolts / 1000,
                            channelStatus = heartbeat.ChannelsStatus
                        };

                        await _webSocket.Broadcast(payload);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing time domain data.");
            }
        }

        private async Task SendFFTDataToSubscribers(CancellationToken cancellationToken)
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

                        var payload = new
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
                        };

                        //  Enviar apenas para clientes que solicitaram FFT
                        await _webSocket.SendToFilteredClients(payload,
                            client => client.RequestedUI == RequestedUI.FFT && client.RequestedSensorMac == sensorMac);
                    }
                }
            }
            catch (OperationCanceledException) { _fftQueue.Clear(); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing FFT data.");
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
