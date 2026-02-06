


using MQTTnet;
using System.Diagnostics;

namespace IIoTVale.Backend.SendTelemetryTester
{
    internal class Program
    {
        private static bool breakAllTasksSafety = false;
        private static bool finishMain = false;
        private static int sensorPrinting = 0;
        static int Main(string[] args)
        {
            while (true)
            {
                Console.WriteLine("Digite a quantidade de senders:");
                var senders = Console.ReadLine();

                Console.WriteLine("Digite a taxa máxima de envio em hertz: ");
                var maxHz = Console.ReadLine();

                if (int.TryParse(senders, out int sendersOut) && int.TryParse(maxHz, out int maxHzOut))
                {
                    main3(sendersOut, maxHzOut);
                    while (!finishMain) ;
                    finishMain = false;
                    Console.Clear();
                }
                else
                {
                    Console.WriteLine("Erro nos parametros.");
                    return 1;
                }
            }
        }
        static async void main2(int senders, int maxHz)
        {
            var stoppingToken = new CancellationTokenSource().Token;

            MqttClientFactory factory = new();
            List<IMqttClient> mqttClients = [];
            List<MqttClientOptions> mqttOptions = [];

            for (int i = 0; i < senders; i++)
            {
                mqttClients.Add(factory.CreateMqttClient());
            }

            try
            {
                for (int i = 0; i < senders; i++)
                {
                    mqttOptions.Add(new MqttClientOptionsBuilder()
                        .WithTcpServer("localhost", 1883)
                        .WithClientId($"Backend_TestWriting_{i}")
                        .WithCleanStart()
                        .Build());
                }
                for (int i = 0; i < senders; i++)
                {
                    await mqttClients[i].ConnectAsync(mqttOptions[i], stoppingToken);
                }
                Console.WriteLine("ESCREVEDORES CONECTADOS");
                //await mqttClient.SubscribeAsync(topic: "telemetry/sensors/#", cancellationToken: stoppingToken);

                

                byte count = 0;
                var releaseSignal = new TaskCompletionSource<bool>();
                foreach (MqttClient mqttClient in mqttClients)
                {
                    byte snapShot = count;
#pragma warning disable CS4014 // Como esta chamada não é esperada, a execução do método atual continua antes de a chamada ser concluída
                    Task.Run(async () =>
                    {
                        await releaseSignal.Task; // Segurar para rodar todos ao mesmo tempo

                        TimeSpan interval = TimeSpan.FromMilliseconds(1000.0 / maxHz);

                        byte[] payload = new byte[11]; // 0xFF, 0xFF, snapshot, 8 bytes de long
                        payload[0] = 0xFF;
                        payload[1] = 0xFF;
                        payload[2] = snapShot;

                        int counter = 0;
                        double hz = 0;
                        var watch = Stopwatch.StartNew();
                        var loopWatch = Stopwatch.StartNew();
                        while (!stoppingToken.IsCancellationRequested)
                        {
                            // 1. Controle de Tempo (Busy Wait de alta precisão)
                            if (loopWatch.Elapsed.TotalMilliseconds < interval.TotalMilliseconds)
                            {
                                // Se estiver muito longe do próximo tick, solta a CPU um tiquinho
                                if (interval.TotalMilliseconds - loopWatch.Elapsed.TotalMilliseconds > 2)
                                    Thread.Sleep(0);
                                continue;
                            }
                            loopWatch.Restart();

                            // 2. Verificação de Saída Rápida
                            if (breakAllTasksSafety) break;

                            // 3. Montagem rápida do payload (sem alocar memória)
                            long ticks = DateTime.Now.ToBinary();
                            // Escreve os bytes do long direto no payload a partir da posição 3
                            BitConverter.TryWriteBytes(payload.AsSpan(3), ticks);

                            // 4. Envio (Fire and Forget para manter Hz estável)
                            _ = mqttClient.PublishBinaryAsync("telemetry/sensors/teste", payload,
                                MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce,
                                cancellationToken: stoppingToken);

                            counter++;

                            // 5. Bloco de Métricas e Logs (roda apenas 1x por segundo)
                            if (watch.ElapsedMilliseconds >= 1000)
                            {
                                hz = counter / (watch.Elapsed.TotalMilliseconds / 1000.0);
                                counter = 0;
                                watch.Restart();

                                // Checa teclado
                                if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                                {
                                    breakAllTasksSafety = true;
                                    sensorPrinting = 0;
                                    break;
                                }


                            }
                            // Lógica de Print Organizada
                            if (sensorPrinting == snapShot)
                            {
                                // Se for o primeiro sensor da fila, limpa a tela para os outros
                                if (snapShot == 0) Console.Clear();

                                Console.WriteLine($"[Sensor {snapShot:D2}] Real: {hz,7:N2} Hz | Alvo: {maxHz} Hz");

                                // Passa o bastão para o próximo sensor imprimir
                                // Interlocked garante que a troca seja segura entre threads
                                Interlocked.Exchange(ref sensorPrinting, (snapShot + 1) % senders);
                            }
                        }

                        //Console.WriteLine("Task encerrada.");
                    });
#pragma warning restore CS4014 // Como esta chamada não é esperada, a execução do método atual continua antes de a chamada ser concluída

                    count++;
                }

                Console.WriteLine("Liberando todos...");
                releaseSignal.SetResult(true);

            }
            catch (Exception ex)
            {
                Console.WriteLine("Erro: " + ex.Message);
            }
        }
        static async Task main3(int senders, int maxHz)
        {
            var cts = new CancellationTokenSource();
            var stoppingToken = cts.Token;
            breakAllTasksSafety = false;
            sensorPrinting = 0;

            MqttClientFactory factory = new();
            List<IMqttClient> mqttClients = new();

            // 1. Conexão inicial
            for (int i = 0; i < senders; i++)
            {
                var client = factory.CreateMqttClient();
                var options = new MqttClientOptionsBuilder()
                    .WithTcpServer("localhost", 1883)
                    .WithClientId($"Backend_TestWriting_{i}")
                    .WithCleanStart()
                    .Build();

                await client.ConnectAsync(options, stoppingToken);
                mqttClients.Add(client);
            }

            Console.Clear();
            Console.CursorVisible = false; // Esconde o cursor para não ficar piscando

            var releaseSignal = new TaskCompletionSource<bool>();
            TimeSpan interval = TimeSpan.FromMilliseconds(1000.0 / maxHz);

            for (int i = 0; i < senders; i++)
            {
                byte snapShot = (byte)i;
                var mqttClient = mqttClients[i];

                _ = Task.Run(async () =>
                {
                    await releaseSignal.Task;

                    byte[] payload = new byte[11];
                    payload[0] = 0xFF; payload[1] = 0xFF; payload[2] = snapShot;

                    int counter = 0;
                    double hz = 0;
                    var metricsWatch = Stopwatch.StartNew();
                    var loopWatch = Stopwatch.StartNew();

                    while (!stoppingToken.IsCancellationRequested)
                    {
                        // --- BUSY WAIT ---
                        if (loopWatch.Elapsed.TotalMilliseconds < interval.TotalMilliseconds)
                        {
                            if (interval.TotalMilliseconds - loopWatch.Elapsed.TotalMilliseconds > 2)
                                Thread.Sleep(0);
                            continue;
                        }
                        loopWatch.Restart();

                        if (breakAllTasksSafety) break;

                        // --- ENVIO ---
                        BitConverter.TryWriteBytes(payload.AsSpan(3), DateTime.Now.ToBinary());
                        _ = mqttClient.PublishBinaryAsync("telemetry/sensors/teste", payload,
                            MQTTnet.Protocol.MqttQualityOfServiceLevel.AtMostOnce);

                        counter++;

                        // --- MÉTRICAS (1x por segundo para o cálculo) ---
                        if (metricsWatch.ElapsedMilliseconds >= 1000)
                        {
                            hz = counter / (metricsWatch.Elapsed.TotalMilliseconds / 1000.0);
                            counter = 0;
                            metricsWatch.Restart();

                            if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                                breakAllTasksSafety = true;
                        }

                        // --- PRINT ESTÁTICO (Roda na velocidade do loop!) ---
                        // Para parecer estática, imprimimos apenas na linha reservada ao sensor
                        if (!breakAllTasksSafety)
                        {
                            // Trava o console para evitar que as threads escrevam por cima uma da outra
                            lock (Console.Out)
                            {
                                Console.SetCursorPosition(0, snapShot);
                                Console.Write($"[Sensor {snapShot:D2}] Real: {hz,7:N2} Hz | Alvo: {maxHz} Hz | Status: TRANSMITINDO   ");
                            }
                        }
                    }
                });
            }

            Console.WriteLine("Pressione 'Q' para parar...");
            await Task.Delay(2000); // Delay curto só para garantir que os Tasks estão prontos
            releaseSignal.SetResult(true);

            while (!breakAllTasksSafety) await Task.Delay(100);

            Console.CursorVisible = true;
            Console.SetCursorPosition(0, senders + 2);
            await cts.CancelAsync();
            Console.Clear();
            Console.WriteLine("Sistema parado.");
            finishMain = true;
        }
    }
}
