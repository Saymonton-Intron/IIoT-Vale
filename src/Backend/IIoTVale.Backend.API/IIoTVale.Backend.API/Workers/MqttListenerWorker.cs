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

                //Console.Write($"Mensagem recebida:");
                //foreach (byte load in payload)
                //{
                //    Console.Write(load + "  ");
                //}
                //Console.WriteLine();
                var timeStamp = DateTime.FromBinary(BitConverter.ToInt64( payload.Skip(3).ToArray(), 0));

                var sendMillis = timeStamp.Millisecond;
                var receiveMillis = DateTime.Now.Millisecond - sendMillis;

                Console.WriteLine(payload[2] + "- " + receiveMillis);
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
                await mqttClient.SubscribeAsync(topic: "telemetry/sensors/#", cancellationToken: stoppingToken);

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
    }
}
