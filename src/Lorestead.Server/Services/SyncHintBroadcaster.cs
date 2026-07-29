using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lorestead.Core.Sync;

namespace Lorestead.Server.Services;

public sealed class SyncHintBroadcaster
{
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new ConcurrentDictionary<Guid, WebSocket>();
    // WebSocket.SendAsync forbids concurrent sends on one socket - broadcasts from
    // parallel uploads serialize through this lock.
    private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);

    public Guid Register(WebSocket socket)
    {
        Guid id = Guid.NewGuid();
        _sockets[id] = socket;
        return id;
    }

    public void Remove(Guid id)
    {
        _sockets.TryRemove(id, out _);
    }

    public async Task Broadcast(long maxSeq)
    {
        byte[] payload = Encoding.UTF8.GetBytes(PayloadJson.Serialize(new SyncHint { MaxSeq = maxSeq }));

        await _sendLock.WaitAsync();
        try
        {
            foreach (KeyValuePair<Guid, WebSocket> pair in _sockets)
            {
                try
                {
                    if (pair.Value.State == WebSocketState.Open)
                    {
                        await pair.Value.SendAsync(new ArraySegment<byte>(payload), WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                }
                catch (Exception)
                {
                    // A dead socket only means that client re-pulls on reconnect.
                    _sockets.TryRemove(pair.Key, out _);
                }
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
