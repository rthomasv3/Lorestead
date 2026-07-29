using System;
using System.Net.WebSockets;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Lorestead.Server.Services;

namespace Lorestead.Server.Endpoints;

public static class SyncSocket
{
    public static void MapSyncSocket(this WebApplication app)
    {
        app.Map("/ws", async (HttpContext context, SyncHintBroadcaster broadcaster) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
            }
            else
            {
                using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
                Guid id = broadcaster.Register(socket);

                try
                {
                    // Hints flow server to client only; the read loop exists to notice
                    // the close handshake (or a dropped connection) and unregister.
                    byte[] buffer = new byte[1024];
                    bool open = true;
                    while (open && socket.State == WebSocketState.Open)
                    {
                        WebSocketReceiveResult received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
                        open = received.MessageType != WebSocketMessageType.Close;
                    }

                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Server shutdown or client vanished mid-receive - nothing to do.
                }
                catch (WebSocketException)
                {
                    // Abrupt client disconnect - the socket is already unusable.
                }
                finally
                {
                    broadcaster.Remove(id);
                }
            }
        });
    }
}
