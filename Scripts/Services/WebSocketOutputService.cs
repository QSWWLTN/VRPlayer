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
	
	// 核心修复：换回无阻塞的 ConcurrentQueue
	private ConcurrentQueue<string>? _sendQueue;

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

		try
		{
			DisconnectInternal();
			IsConnecting = true;
			OnConnectionChanged?.Invoke(false);

			_ws = new ClientWebSocket();
			_cts = new CancellationTokenSource(timeoutMs);

			var uri = new Uri($"ws://{Host}:{Port}/ws"); 
			//Log($"Connecting to {uri}...");

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

			Log($"WebSocket connected to {Host}:{Port}");
			OnConnectionChanged?.Invoke(true);

			// 启动收发双异步循环
			_ = ReceiveLoopAsync(_cts.Token);
			_ = SendLoopAsync(_cts.Token);

			return true;
		}
		catch (Exception ex)
		{
			//Log($"WS connect failed: {ex.Message}");
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
				//Log($"WS received #{ReceivedCount}: {msg}");
			}
		}
		catch { }
		finally
		{
			if (_ws?.State is WebSocketState.Open or WebSocketState.CloseReceived)
				DisconnectInternal();
		}
	}

	// 核心修复：纯异步发包循环，绝不阻塞 Godot 主线程！
	private async Task SendLoopAsync(CancellationToken token)
	{
		try
		{
			while (_ws?.State == WebSocketState.Open && !token.IsCancellationRequested)
			{
				if (_sendQueue != null && _sendQueue.TryDequeue(out var msg))
				{
					var bytes = Encoding.UTF8.GetBytes(msg);
					await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
					SentCount++;
					
					// 防洪控制：发送成功后，给 ESP32 芯片预留 20ms 的消化时间 (最高 50Hz 发送率)
					await Task.Delay(20, token); 
				}
				else
				{
					// 队列为空时，异步休眠 10ms，让出 CPU 执行权，绝对不卡死画面
					await Task.Delay(10, token);
				}
			}
		}
		catch (OperationCanceledException) { }
		catch (Exception ex)
		{
			//Log($"WS Send error: {ex.Message}");
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
		
		// 清空队列
		if (_sendQueue != null)
		{
			while (_sendQueue.TryDequeue(out _)) { }
		}
		_sendQueue = null;
		
		_cts?.Dispose();
		_cts = null;

		if (!_disposed)
		{
			OnConnectionChanged?.Invoke(false);
			//Log("WebSocket disconnected");
		}
	}

	public void SendPosition(double percentage, int maxPercentage)
	{
		if (!IsConnected || _sendQueue == null) return;

		const int outputMaximum = 999;
		double scaled = percentage * maxPercentage / 100.0;
		int value = Math.Clamp((int)(scaled / 100.0 * outputMaximum), 0, outputMaximum);
		
		var cmd = $"L0{value:D3}I30\n";
		
		// 防止队列无限积压：如果积压过多，抛弃老旧数据，始终让硬件追赶最新画面
		if (_sendQueue.Count >= 60)
		{
			_sendQueue.TryDequeue(out _);
		}
		
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
