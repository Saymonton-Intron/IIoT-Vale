using IIoTVale.Backend.API.Wrappers;
using IIoTVale.Backend.Core.DTOs;
using IIoTVale.Backend.Core.Enums;
using MQTTnet;
using System.Buffers;
using System.Text;
using System.Threading.Channels;
namespace IIoTVale.Backend.API.Workers
{
    public class MqttListenerWorker : BackgroundService
    {
        private ILogger<MqttListenerWorker> _logger;
        private bool isReconnecting = false;
        private int lastSequenceId = 0;
        private readonly string HOST = "localhost";
        private readonly int PORT = 1883;
        private readonly string CLIENT_ID = "ApiBackend_Listener";
        private readonly ChannelWriter<ITelemetryDto> _Dbwriter;
        private readonly ChannelWriter<ITelemetryDto> _Uiwriter;
        public MqttListenerWorker(ILogger<MqttListenerWorker> logger, DbChannel dbChannel, UiChannel uiChannel)
        {
            _logger = logger;
            _Dbwriter = dbChannel.Writer;
            _Uiwriter = uiChannel.Writer;
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
                ITelemetryDto telemetry = ProcessPayload(e.ApplicationMessage.Topic, payload);
                if (telemetry is DataStreamingDto streaming)
                {
                    await _Dbwriter.WriteAsync(streaming);
                    await _Uiwriter.WriteAsync(streaming);
                }
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
        private ITelemetryDto ProcessPayload(string topic, byte[] payload)
        {
            int slashPosition = topic.IndexOf('/');
            if (slashPosition == -1)
            {
                return null; // por enquanto só processamos dados vindos do sensor
            }
            string sensorMAC = topic[..slashPosition];
            try
            {
                if (payload[1] == 0x0E) //HeartBeat
                {
                    return new HeartbeatTelemetryDto()
                    {
                        SensorMAC = sensorMAC,
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
                            SensorMAC = sensorMAC,
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
                else if (payload[0] == 0x01 && payload[1] == 0x03) //Streaming mode continuous
                {
                    var frequency = BitConverter.ToInt16([payload[8], payload[9]], 0);
                    if (frequency == 0) frequency = 200; //Valor padrão

                    double timeStepMs = 1000.0 / frequency; // define o passo ex: 200Hz = 5ms

                    var timestamp = BitConverter.ToInt32([payload[2], payload[3], payload[4], payload[5]], 0);
                    var milliSec = BitConverter.ToInt16([payload[6], payload[7]], 0);

                    var samplesInPackage = BitConverter.ToInt16([payload[17], payload[18]], 0);

                    DateTime packageArrivelTime = DateTime.UtcNow;
                    DateTime baseTime = packageArrivelTime.AddMilliseconds(-(samplesInPackage * timeStepMs)); // define um tempo base para distribuir os pacotes no tempo

                    int currentSeq = BitConverter.ToInt32([payload[14], payload[15], payload[16], 0], 0);

                    int lostSequence = currentSeq - lastSequenceId;
                    if (lostSequence > 1)
                    {
                        int lost = lostSequence - 1;
                        _logger.LogError("{lost} packages was lost.", lost);
                    }
                    lastSequenceId = currentSeq;

                    List<DataModel> dataModels = [];
                    for (int i = 29; i <= payload.Length - 9; i += 9)
                    {
                        dataModels.Add(new()
                        {
                            AccZ = Read3Bytes(payload, i) / 1000.0,
                            AccX = Read3Bytes(payload, i + 3) / 1000.0,
                            AccY = Read3Bytes(payload, i + 6) / 1000.0,
                            SampleTime = baseTime.AddMilliseconds((dataModels.Count + 1) * timeStepMs)
                        });
                    }
                    return new DataStreamingDto()
                    {
                        SensorMAC = sensorMAC,
                        DataModel = dataModels,
                        Frequency = frequency,
                        TimeStamp = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(timestamp).AddMilliseconds(milliSec),
                    };
                    // Aviso pro futuro:
                    // Quando 1 eixo está desabilitado, a lógica do for não quebra mas os valores serão inconsistentes.
                    // Estamos usando o horário do servidor pois o acelerometro não manda horario por amostra.
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payload...");
            }
            
            return null;
        }
        private static double Read3Bytes(byte[] buffer, int offset)
        {
            byte b0 = buffer[offset];     // Low
            byte b1 = buffer[offset + 1]; // Mid
            byte b2 = buffer[offset + 2]; // High + Sign

            // Verifica o sinal (Bit 7 do terceiro byte)
            bool negativo = (b2 & 0x80) != 0;

            // Remove o bit de sinal para pegar a magnitude
            int highByteLimpo = b2 & 0x7F;

            // Monta o valor: (High << 16) | (Mid << 8) | Low
            int magnitude = (highByteLimpo << 16) | (b1 << 8) | b0;

            // Aplica o sinal
            return negativo ? -magnitude : magnitude;
        }
        private static void DebugTelemetryOnConsole(byte[] payload, string topic, ITelemetryDto telemetry)
        {
            if (telemetry is null)
            {
                Console.Clear();
                Console.Write($"RECEBIDO EM {topic} bytes lenhth {payload.Length}: ");
                int count = 0;
                foreach (byte load in payload)
                {

                    Console.Write(count + ":");

                    Console.Write($"0x{load:X2}" + " ");

                    count++;
                }
                Console.WriteLine();
                return;
            }
            switch (telemetry.TelemetryMode)
            {
                case TelemetryMode.UPDATE:
                    if (telemetry is HeartbeatTelemetryDto heartbeat)
                    {
                        //return;
                        Console.Clear();
                        Console.WriteLine("Sensor: " + heartbeat.SensorMAC);
                        Console.WriteLine();
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
                        Console.WriteLine("Sensor: " + daqProfile.SensorMAC);
                        Console.WriteLine();
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
                    else if (telemetry is DataStreamingDto dataStreaming)
                    {
                        Console.Clear();
                        Console.WriteLine($"Aquisicionando dados a {dataStreaming.Frequency}Hz do sensor {dataStreaming.SensorMAC}");
                        Console.WriteLine();
                        Console.Write("Acc Z    " + dataStreaming.DataModel[0].AccZ);

                        Console.WriteLine();
                        Console.Write("Acc Y    " + dataStreaming.DataModel[0].AccY);

                        Console.WriteLine();
                        Console.WriteLine("Acc X    " + dataStreaming.DataModel[0].AccX);

                        Console.WriteLine();
                        Console.WriteLine("Inicio da aquisição: " + dataStreaming.TimeStamp.ToString("dd/MM/yyyy 'às' HH:mm:ss 'UTC'"));

                        Console.WriteLine();
                        //Console.WriteLine("Num de pacotes: " + dataStreaming.NumSamples);
                        Console.WriteLine("Tamanho da lista: " + dataStreaming.DataModel.Count);
                        Console.WriteLine();
                        //Console.WriteLine("SequenceID: " + dataStreaming.SequenceID);
                    }
                    break;
            }
        }
    }
}
