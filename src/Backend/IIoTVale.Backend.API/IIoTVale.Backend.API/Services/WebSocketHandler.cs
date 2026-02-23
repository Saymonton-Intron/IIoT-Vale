using IIoTVale.Backend.API.Workers;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace IIoTVale.Backend.API.Services
{
    // Estado individual de cada cliente
    public class WebSocketClient
    {
        public WebSocket Socket { get; set; }
        public string ClientId { get; set; }
        public string RequestedSensorMac { get; set; }
        public RequestedUI RequestedUI { get; set; }

        public class WebSocketHandler
        {
            private readonly ConcurrentDictionary<string, WebSocketClient> _clients = new();

            public bool AnyConnectionAvailable => _clients.Count > 0;

            public async Task AddConnection(WebSocket socket)
            {
                var clientId = Guid.NewGuid().ToString();
                var client = new WebSocketClient
                {
                    Socket = socket,
                    ClientId = clientId
                };

                _clients.TryAdd(clientId, client);
                Console.WriteLine($"Cliente {clientId} conectado. Total: {_clients.Count}");

                // Mantém a conexão aberta e processa mensagens do cliente
                await HandleClientMessages(client);
            }

            private async Task HandleClientMessages(WebSocketClient client)
            {
                byte[] buffer = new byte[1024 * 4];

                try
                {
                    while (client.Socket.State == WebSocketState.Open)
                    {
                        WebSocketReceiveResult result = await client.Socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer), CancellationToken.None);

                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await client.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                                    "Closed by client", CancellationToken.None);
                            break;
                        }

                        //  PROCESSAR MENSAGENS DO CLIENTE
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                            await ProcessClientMessage(client, message);
                        }
                    }
                }
                catch (WebSocketException) { }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro no cliente {client.ClientId}: {ex.Message}");
                }
                finally
                {
                    _clients.TryRemove(client.ClientId, out _);
                    if (client.Socket.State != WebSocketState.Aborted)
                    {
                        client.Socket.Dispose();
                    }
                    Console.WriteLine($"Cliente {client.ClientId} desconectado. Total: {_clients.Count}");
                }
            }

            private async Task ProcessClientMessage(WebSocketClient client, string message)
            {
                try
                {
                    var request = JsonSerializer.Deserialize<ClientRequest>(message);

                    switch (request?.Type)
                    {
                        case "set_ui_mode":
                            client.RequestedUI = request.RequestedUI;
                            client.RequestedSensorMac = request.RequestedSensorMac;
                            //Console.WriteLine($"Cliente {client.ClientId} alterou UI para {client.RequestedUI}");
                            break;

                            // Configurações COMPARTILHADAS (broadcast)
                            //case "set_alarm_config":
                            //    await BroadcastSharedConfig(request);
                            //    break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erro ao processar mensagem: {ex.Message}");
                }
            }

            //  Broadcast GERAL (todos recebem)
            public async Task Broadcast(object data)
            {
                if (!AnyConnectionAvailable) return;

                var json = JsonSerializer.Serialize(data);
                var bytes = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(bytes);

                var tasks = _clients.Values
                    .Where(c => c.Socket?.State == WebSocketState.Open)
                    .Select(c => c.Socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None));

                await Task.WhenAll(tasks);
            }

            //  Envio SELETIVO (baseado em filtros por cliente)
            public async Task SendToFilteredClients(object data, Func<WebSocketClient, bool> filter)
            {
                if (!AnyConnectionAvailable) return;

                var json = JsonSerializer.Serialize(data);
                var bytes = Encoding.UTF8.GetBytes(json);
                var segment = new ArraySegment<byte>(bytes);

                var tasks = _clients.Values
                    .Where(c => c.Socket?.State == WebSocketState.Open && filter(c))
                    .Select(c => c.Socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None));

                await Task.WhenAll(tasks);
            }

            //  Broadcast de configurações compartilhadas
            private async Task BroadcastSharedConfig(ClientRequest request)
            {
                //await Broadcast(new
                //{
                //    Type = "shared_config_updated",
                //    ConfigType = request.Type,
                //    Value = request.Value
                //});
            }

            // Obtém clientes que estão solicitando determinado tipo de UI
            public IEnumerable<WebSocketClient> GetClientsByUIMode(RequestedUI mode)
            {
                return _clients.Values.Where(c => c.RequestedUI == mode);
            }
        }

        // DTO para mensagens do cliente
        public class ClientRequest
        {
            public string Type { get; set; }
            public string RequestedSensorMac { get; set; }
            public RequestedUI RequestedUI { get; set; }
        }
    }
}
