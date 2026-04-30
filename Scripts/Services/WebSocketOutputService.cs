using System.Net.WebSockets;
using System.Text;

namespace VRPlayerProject.Services;

public class WebSocketOutputService : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private bool _disposed;

    public bool IsConnected => _ws?.State == WebSocketState.Open;
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; } = 80;
    public int SentCount { get; private set; }
    public int ReceivedCount { get; private set; }
    public bool IsConnecting { get; private set; }

    public event Action<string>? OnLog;
    public event Action<bool>? OnConnectionChanged;

    public void SetHost(string host) => Host = host;
    public void SetPort(int port) => Port = port;

    private enum WsState { Idle, Connecting, Connected, Disconnected }

    public async Task<bool> Connect(int timeoutMs = 5000)
    {
        if (IsConnecting)
        {
            Log("Already connecting...");
            return false;
        }

        try
        {
            DisconnectInternal();

            IsConnecting = true;
            OnConnectionChanged?.Invoke(false);

            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource(timeoutMs);

            var uri = new Uri($"ws://{Host}:{Port}/ws");
            Log($"Connecting to {uri}...");

            await _ws.ConnectAsync(uri, _cts.Token);

            if (_ws.State != WebSocketState.Open)
            {
                Log("WebSocket connection failed: not open after connect");
                IsConnecting = false;
                DisconnectInternal();
                return false;
            }

            IsConnecting = false;
            SentCount = 0;
            ReceivedCount = 0;

            Log($"WebSocket connected to {Host}:{Port}");
            OnConnectionChanged?.Invoke(true);

            _ = ReceiveLoopAsync();
            return true;
        }
        catch (OperationCanceledException)
        {
            Log("WS connection timed out (5s)");
            IsConnecting = false;
            DisconnectInternal();
            return false;
        }
        catch (Exception ex)
        {
            Log($"WS connect failed: {ex.Message}");
            IsConnecting = false;
            DisconnectInternal();
            return false;
        }
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[4096];
        try
        {
            while (_ws?.State == WebSocketState.Open && _cts != null && !_cts.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                ReceivedCount++;
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Log($"WS received #{ReceivedCount}: {msg}");
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            Log($"WS receive error: {ex.Message}");
        }
        finally
        {
            if (_ws?.State is WebSocketState.Open or WebSocketState.CloseReceived)
                DisconnectInternal();
        }
    }

    public void Disconnect()
    {
        if (!IsConnected && !IsConnecting) return;
        IsConnecting = false;
        DisconnectInternal();
    }

    private void DisconnectInternal()
    {
        try
        {
            _cts?.Cancel();
        }
        catch { }

        if (_ws?.State == WebSocketState.Open || _ws?.State == WebSocketState.CloseReceived)
        {
            try
            {
                _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch { }
        }

        _ws?.Dispose();
        _ws = null;

        _cts?.Dispose();
        _cts = null;

        SentCount = 0;
        ReceivedCount = 0;

        if (!_disposed)
        {
            OnConnectionChanged?.Invoke(false);
            Log("WebSocket disconnected");
        }
    }

    public bool Send(string message)
    {
        if (_ws?.State != WebSocketState.Open) return false;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None)
                .GetAwaiter().GetResult();
            SentCount++;
            return true;
        }
        catch (Exception ex)
        {
            Log($"WS send failed: {ex.Message}");
            return false;
        }
    }

    public void SendPosition(double percentage, int maxPercentage)
    {
        const int outputMaximum = 999;
        double scaled = percentage * maxPercentage / 100.0;
        int value = Math.Clamp((int)(scaled / 100.0 * outputMaximum), 0, outputMaximum);
        Send($"L0{value:D3}I1\n");
    }

    private void Log(string message)
    {
        OnLog?.Invoke(message);
    }

    public void Dispose()
    {
        _disposed = true;
        DisconnectInternal();
    }
}
