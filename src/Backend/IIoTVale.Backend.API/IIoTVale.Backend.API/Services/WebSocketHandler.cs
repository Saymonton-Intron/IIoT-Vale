using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace IIoTVale.Backend.API.Services
{
    public class WebSocketHandler
    {
        private readonly List<WebSocket> _connections = new();
        public bool AnyConnectionAvailable => _connections.Count > 0;

        public async Task AddConnection(WebSocket socket)
        {
            _connections.Add(socket);
            Console.WriteLine($"Novo cliente conectado. Total: {_connections.Count}");

            // Mantém a conexão aberta até o cliente fechar
            await KeepConnectionAlive(socket);
        }

        private async Task KeepConnectionAlive(WebSocket socket)
        {
            byte[] buffer = new byte[1024 * 4];

            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by client", CancellationToken.None);
                        break;
                    }
                }
            }
            catch (WebSocketException)
            {
                // Captura desconexões forçadas (Abortive close)
            }
            catch (Exception)
            {
                // Captura outros erros genéricos de I/O
            }
            finally
            {
                _connections.Remove(socket);
                if (socket.State != WebSocketState.Aborted)
                {
                    socket.Dispose();
                }
            }
        }
        public async Task Broadcast(object data)
        {
            if (!AnyConnectionAvailable) return;
            var json = JsonSerializer.Serialize(data);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            // Envia para todos os "WebSockets" na lista
            foreach (var socket in _connections.Where(s => s?.State == WebSocketState.Open))
            {
                await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }
}
