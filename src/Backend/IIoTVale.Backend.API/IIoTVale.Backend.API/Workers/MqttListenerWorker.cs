using IIoTVale.Backend.Core.DTOs;
using MQTTnet;
using System.Buffers;
using System.Text;
namespace IIoTVale.Backend.API.Workers
{
    public class MqttListenerWorker : BackgroundService
    {
        private ILogger<MqttListenerWorker> _logger;
        private readonly string HOST = "localhost";
        private readonly int PORT = 1883;
        private readonly string CLIENT_ID = "ApiBackend_Listener";
        private readonly byte[] HeartbeatHeader = [0x2F, 0x0E, 0x01, 0xF4, 0x5E, 0xAB, 0x98, 0x7C, 0x5B, 0x00, 0x00, 0x07, 0x05, 0xFF, 0xFE, 0x1F, 0x09];
        private readonly byte[] DAQProfileHeader = [0x2B, 0x4F, 0x01, 0xF4, 0x5E, 0xAB, 0x98, 0x7C, 0x5B, 0x00, 0x00, 0x07, 0x05, 0xFF, 0xFE, 0x19, 0x1B];
        public MqttListenerWorker(ILogger<MqttListenerWorker> logger)
        {
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            async Task reconnection(IMqttClient client, MqttClientOptions options)
            {
                while (!client.IsConnected)
                {
                    try
                    {
                        await client.ConnectAsync(options, stoppingToken);
                    }
                    catch (MQTTnet.Exceptions.MqttCommunicationException)
                    {
                        _logger.LogInformation("Connection lost, trying reconnection...");
                        await Task.Delay(1000, stoppingToken);
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
            }
            _logger.LogInformation("Initializating {CLIENT_ID} mqtt client.", CLIENT_ID);
            Console.WriteLine("Inicializando cliente MQTT...");
            MqttClientFactory mqttFactory = new();
            using IMqttClient mqttClient = mqttFactory.CreateMqttClient();

            MqttClientOptions options = new MqttClientOptionsBuilder()
                .WithTcpServer(HOST, PORT) // Mosquitto docker
                .WithClientId(CLIENT_ID)
                .WithCleanStart(false)
                .Build();

            mqttClient.ApplicationMessageReceivedAsync += async e =>
            {
                var payload = e.ApplicationMessage.Payload.ToArray();
                // Implement...
                ITelemetryDto telemetry = ProcessPayload(payload);
                if (telemetry is null) return;
                switch (telemetry.TelemetryMode)
                {
                    case TelemetryMode.UPDATE:
                        if (telemetry is HeartbeatTelemetryDto heartbeat)
                        {
                            Console.Clear();
                            Console.WriteLine($"Horário do sensor: {heartbeat.DateAndTime.ToString()}");
                            Console.WriteLine();
                            Console.WriteLine($"Status de rede");   
                            Console.WriteLine($"PER: {heartbeat.NetworkStatus.ErrorRate}");
                            Console.WriteLine($"LQI: {heartbeat.NetworkStatus.LQI}");
                            Console.WriteLine();
                            Console.WriteLine($"Temperatura: {heartbeat.NetworkStatus.InternalTemperature}ºC");
                            Console.WriteLine($"");
                            Console.WriteLine($"Memoria e energia");
                            Console.WriteLine($"Memoria livre do datalogger: {heartbeat.DataLoggerMemoryPercentage}%");
                            Console.WriteLine($"Tensão da bateria: {heartbeat.BatteryVolts/1000}V");
                            Console.WriteLine($"");
                            Console.WriteLine($"Sensores");
                            Console.WriteLine($"Canais disponiveis: {heartbeat.AvailableChannels}");
                            Console.WriteLine($"Canal um: {heartbeat.ChannelsStatus[0]}");
                            Console.WriteLine($"Canal dois: {heartbeat.ChannelsStatus[1]}");
                            Console.WriteLine($"Canal tres: {heartbeat.ChannelsStatus[2]}");
                        }
                        break;
                    case TelemetryMode.CREATE:
                        if (telemetry is DaqProfileTelemetryDto daqProfile)
                        {
                            Console.WriteLine("CREATE RECEIVED...");
                        }
                        break;
                }

                //Console.Write($"RECEBIDO EM {e.ApplicationMessage.Topic}: ");
                //foreach (byte load in payload)
                //{
                //    Console.Write($"0x{load:X2}"+ " ");
                //}
                //Console.WriteLine();
            };

            mqttClient.DisconnectedAsync += async e =>
            {
                await reconnection(mqttClient, options);
            };

            try
            {
                _logger.LogInformation("Trying to stabilish a connection to host...");
                await reconnection(mqttClient, options);

                Console.WriteLine("Se inscrevendo no tópico...");
                await mqttClient.SubscribeAsync(topic: "F45EAB987C5B0000/#", cancellationToken: stoppingToken);

                Console.WriteLine("Aguardando dados.");
                while (!stoppingToken.IsCancellationRequested) // Só para "travar" a execução, não há mais nada a ser fazido
                {
                    await Task.Delay(5000, stoppingToken);
                }
            }
            catch (MQTTnet.Exceptions.MqttCommunicationException mqttEx)
            {
                _logger.LogError(mqttEx, "An error occured while connecting host {HOST}, trying reconnection...", HOST);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Client MQTT disconnected, closing service...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An unknown error occured.");
                throw;
            }
        }
        private ITelemetryDto ProcessPayload(byte[] payload)
        {
            var payloadHeader = payload.Take(17).ToArray();
            if (payloadHeader.SequenceEqual(HeartbeatHeader))
            {
                return new HeartbeatTelemetryDto()
                {
                    SensorType = Core.Enums.SensorType.ACCELEROMETER,
                    DateAndTime = new DateTime(BitConverter.ToInt16([payload[17], payload[18]], 0), payload[19], payload[20], payload[21], payload[22], payload[23]),
                    NetworkStatus = new()
                    {
                        ErrorRate = BitConverter.ToInt16([payload[26], payload[27]]),
                        LQI = payload[28],
                        InternalTemperature = BitConverter.ToInt16([payload[33], payload[34]]) / 2
                    },
                    DataLoggerMemoryPercentage = (payload[37] / 200.0) * 100.0,
                    BatteryVolts = BitConverter.ToInt16([payload[42], payload[43]]),
                    AvailableChannels = payload[44],
                    ChannelsStatus = [payload[45], payload[46], payload[47]],
                };
            }
            else if (payloadHeader == DAQProfileHeader)
            {
                return null;
            }
            return null;
        }
    }
}
