using System;
using System.Net.WebSockets;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace VRPlayerProject.Services;

public class WebSocketOutputService : IDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private ConcurrentQueue<string>? _sendQueue;

    // --- 新增：死区过滤记录 ---
    private int _lastEnqueuedValue = -999;
    private const int MinDeltaThreshold = 3; 

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

    public async Task<bool> Connect(int timeoutMs = 5000)
    {
        if (IsConnecting) return false;
        DisconnectInternal();
        IsConnecting = true;
        OnConnectionChanged?.Invoke(false);

        _ws = new ClientWebSocket();
        // 增加心跳保活
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(5);

        _cts = new CancellationTokenSource(timeoutMs);
        var uri = new Uri($"ws://{Host}:{Port}/ws");

        try
        {
            await _ws.ConnectAsync(uri, _cts.Token);
            if (_ws.State != WebSocketState.Open)
            {
                IsConnecting = false;
                DisconnectInternal();
                return false;
            }

            IsConnecting = false;
            SentCount = 0;
            ReceivedCount = 0;
            _cts = new CancellationTokenSource();
            _sendQueue = new ConcurrentQueue<string>();
            _lastEnqueuedValue = -999;

            Log($"WebSocket connected to {Host}:{Port}");
            OnConnectionChanged?.Invoke(true);

            _ = ReceiveLoopAsync(_cts.Token);
            _ = SendLoopAsync(_cts.Token);
            return true;
        }
        catch (Exception ex)
        {
            IsConnecting = false;
            DisconnectInternal();
            return false;
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var buffer = new byte[4096];
        try
        {
            while (_ws?.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close) break;

                ReceivedCount++;
                var msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
            }
        }
        catch { }
        finally
        {
            if (_ws?.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                DisconnectInternal();
            }
        }
    }

    private async Task SendLoopAsync(CancellationToken token)
    {
        while (_ws?.State == WebSocketState.Open && !token.IsCancellationRequested)
        {
            if (_sendQueue != null && _sendQueue.TryDequeue(out var msg))
            {
                // 抛弃旧帧，只发最新队列尾部的一帧
                while (_sendQueue.TryDequeue(out var newerMsg))
                {
                    msg = newerMsg;
                }

                try
                {
                    var bytes = Encoding.UTF8.GetBytes(msg);
                    await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
                    SentCount++;
                }
                catch { }

                // 降低发送频率到 30Hz，防止 ESP32 崩溃
                await Task.Delay(33, token); 
            }
            else
            {
                await Task.Delay(10, token);
            }
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
        try { _cts?.Cancel(); } catch { }

        if (_ws?.State == WebSocketState.Open || _ws?.State == WebSocketState.CloseReceived)
        {
            try { _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).GetAwaiter().GetResult(); } catch { }
        }

        _ws?.Dispose();
        _ws = null;

        if (_sendQueue != null)
        {
            while (_sendQueue.TryDequeue(out _)) { }
        }
        _sendQueue = null;

        _cts?.Dispose();
        _cts = null;

        if (!_disposed) OnConnectionChanged?.Invoke(false);
    }

    public void SendPosition(double percentage, int maxPercentage)
    {
        if (!IsConnected || _sendQueue == null) return;

        const int outputMaximum = 999;
        double scaled = percentage * maxPercentage / 100.0;
        int value = Math.Clamp((int)(scaled / 100.0 * outputMaximum), 0, outputMaximum);

        // 死区过滤：变动过小不发包
        if (Math.Abs(value - _lastEnqueuedValue) < MinDeltaThreshold)
        {
            return;
        }

        _lastEnqueuedValue = value;

        // 这里的 I33 配合 33ms 循环，平滑物理过渡
        var cmd = $"L0{value:D3}I33\n";
        _sendQueue.Enqueue(cmd);
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