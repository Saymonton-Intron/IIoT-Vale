using IIoTVale.Backend.Core.DTOs;
using IIoTVale.Backend.Core.Enums;
using MQTTnet;
using System.Buffers;
using System.Text;
namespace IIoTVale.Backend.API.Workers
{
    public class MqttListenerWorker : BackgroundService
    {
        private ILogger<MqttListenerWorker> _logger;
        private bool isReconnecting = false;
        private readonly string HOST = "localhost";
        private readonly int PORT = 1883;
        private readonly string CLIENT_ID = "ApiBackend_Listener";
        public MqttListenerWorker(ILogger<MqttListenerWorker> logger)
        {
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            async Task reconnection(IMqttClient client, MqttClientOptions options)
            {
                while (!client.IsConnected && !isReconnecting)
                {
                    try
                    {
                        isReconnecting = true;
                        await client.ConnectAsync(options, stoppingToken);
                    }
                    catch (MQTTnet.Exceptions.MqttCommunicationException)
                    {
                        _logger.LogInformation("Connection lost, trying reconnection...");
                        await Task.Delay(5000, stoppingToken);
                    }
                    catch (Exception)
                    {
                        isReconnecting = false;
                        throw;
                    }
                }
                isReconnecting = false;
            }
            _logger.LogInformation("Initializating {CLIENT_ID} mqtt client.", CLIENT_ID);
            //Console.WriteLine("Inicializando cliente MQTT...");
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
                            //return;
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
                            Console.WriteLine($"Tensão da bateria: {heartbeat.BatteryVolts / 1000}V");
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
                            var daqMode = daqProfile.DaqMode switch
                            {
                                DaqMode.STREAMING => "STREAMING",
                                DaqMode.COMMISSIONING => "TA PARADO",
                                _ => "TAPORRA"
                            };
                            var type = daqProfile.DaqOptions.StreamingType switch
                            {
                                StreamingType.CONTINUOUS => "Continuous",
                                StreamingType.BURST => "Burst",
                                StreamingType.ONE_SHOT => "One shota",
                                _ => "ERRO"
                            };
                            Console.Clear();
                            Console.WriteLine($"DAQ MODE: {daqMode}");
                            Console.WriteLine();
                            Console.WriteLine($"DAQ OPTIONS");
                            Console.WriteLine($"Datalogger:  {daqProfile.DaqOptions.DataLogger.ToString()}");
                            Console.WriteLine($"Store & Forward:  {daqProfile.DaqOptions.StoreAndForward.ToString()}");
                            Console.WriteLine($"Streaming type: {type}");
                            Console.WriteLine($"Transmission:  {daqProfile.DaqOptions.Transmission.ToString()}");
                            Console.WriteLine($"Stand Alone: {daqProfile.DaqOptions.StandAlone.ToString()}");
                            Console.WriteLine();
                            Console.WriteLine($"MAX TX: {daqProfile.MaxSampleRate}");
                            Console.WriteLine($"CURRENT TX: {daqProfile.SampleRate}");
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

                _logger.LogInformation("Subscribing to topic...");
                await mqttClient.SubscribeAsync(topic: "F45EAB987C5B0000/#", cancellationToken: stoppingToken);

                _logger.LogInformation("Waiting for data...");
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
            if (payload[1] == 0x0E) //HeartBeat
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
            else if (payload[1] == 0x4F) // 0x4F CREATE PROFILE | 0x10 DAQ
            {
                if (payload[17] == 0x10) // testa se é DAQ profile
                {
                    var DaqOptions = BitConverter.ToInt16([payload[19], payload[20]], 0);
                    bool bit2 = (DaqOptions & (1 << 2)) != 0;
                    bool bit3 = (DaqOptions & (1 << 3)) != 0;
                    bool bit4 = (DaqOptions & (1 << 4)) != 0;

                    StreamingType type;
                    if (bit2 && !bit3 && !bit4) type = StreamingType.CONTINUOUS;
                    else if (bit3 && !bit2 && !bit4) type = StreamingType.ONE_SHOT;
                    else if (bit4 && bit3 && !bit2) type = StreamingType.BURST;
                    else throw new NotImplementedException();

                    return new DaqProfileTelemetryDto()
                    {
                        DaqMode = payload[18] switch
                        {
                            0x01 => DaqMode.COMMISSIONING,
                            0x02 => DaqMode.LOW_DUTY_CYCLE,
                            0x03 => DaqMode.STREAMING,
                            0x04 => DaqMode.ALARM,
                            0x05 => DaqMode.SET_MODE,
                            0x06 => DaqMode.SHOCK_DETECTION,
                            _ => throw new NotImplementedException()
                        },
                        DaqOptions = new()
                        {
                            DataLogger = (DaqOptions & (1 << 0)) != 0,
                            StoreAndForward = (DaqOptions & (1 << 1)) != 0,
                            StreamingType = type,
                            Transmission = (DaqOptions & (1 << 5)) != 0,
                            StandAlone = (DaqOptions & (1 << 6)) != 0,
                        },
                        MaxSampleRate = BitConverter.ToInt32([payload[31], payload[32], payload[33], 0], 0),
                        SampleRate = BitConverter.ToInt32([payload[34], payload[35], payload[36], 0], 0) // recurso técnico
                    };
                }
            }
            return null;
        }
    }
}
