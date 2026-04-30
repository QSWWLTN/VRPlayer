#if !ANDROID
using System.IO.Ports;
#endif
using VRPlayerProject.Models;

namespace VRPlayerProject.Services;

public class SerialOutputService : IDisposable
{
#if ANDROID
    public bool IsConnected => false;
    public string? ConnectedPortName => null;
    public event Action<string>? OnLog;
    public event Action<bool>? OnConnectionChanged;
    public string[] GetAvailablePorts() => [];
    public bool Connect(string portName, int baudRate = 115200) => false;
    public void Disconnect() { }
    public bool SendCommand(string command) => false;
    public void SendPosition(double percentage, int maxPercentage, ProtocolType protocol) { }
    public void Dispose() { }
#else
    private SerialPort? _port;
    private bool _isConnected;

    public bool IsConnected => _isConnected;
    public string? ConnectedPortName { get; private set; }

    public event Action<string>? OnLog;
    public event Action<bool>? OnConnectionChanged;

    public string[] GetAvailablePorts() => SerialPort.GetPortNames();

    public bool Connect(string portName, int baudRate = 115200)
    {
        try
        {
            Disconnect();

            _port = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                Handshake = Handshake.None,
                ReadTimeout = 1000,
                WriteTimeout = 1000
            };

            _port.Open();
            _port.DataReceived += OnDataReceived;

            _isConnected = true;
            ConnectedPortName = portName;
            OnLog?.Invoke($"Connected to {portName} ({baudRate} baud)");
            OnConnectionChanged?.Invoke(true);
            return true;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Connection failed: {ex.Message}");
            _isConnected = false;
            ConnectedPortName = null;
            OnConnectionChanged?.Invoke(false);
            return false;
        }
    }

    public void Disconnect()
    {
        if (_port != null && _port.IsOpen)
        {
            try { _port.Close(); } catch { }
            _port.DataReceived -= OnDataReceived;
            _port.Dispose();
            _port = null;
        }

        _isConnected = false;
        ConnectedPortName = null;
        OnConnectionChanged?.Invoke(false);
        OnLog?.Invoke("Serial port disconnected");
    }

    public bool SendCommand(string command)
    {
        if (!_isConnected || _port == null || !_port.IsOpen) return false;
        try
        {
            _port.WriteLine(command);
            return true;
        }
        catch (Exception ex)
        {
            OnLog?.Invoke($"Send failed: {ex.Message}");
            return false;
        }
    }

    public void SendPosition(double percentage, int maxPercentage, ProtocolType protocol)
    {
        if (protocol == ProtocolType.Raw)
            SendRawPosition(percentage, maxPercentage);
        else
            SendTCodePosition(percentage, maxPercentage);
    }

    private void SendTCodePosition(double percentage, int maxPercentage)
    {
        const int outputMaximum = 999;
        double scaled = percentage * maxPercentage / 100.0;
        int value = Math.Clamp((int)(scaled / 100.0 * outputMaximum), 0, outputMaximum);
        SendCommand($"L0{value:D3}I1");
    }

    private void SendRawPosition(double percentage, int maxPercentage)
    {
        double scaled = percentage * maxPercentage / 100.0;
        int value = Math.Clamp((int)scaled, 0, 100);
        SendCommand($"L0:{value}");
    }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        if (_port == null || !_port.IsOpen) return;
        try
        {
            string data = _port.ReadExisting();
            OnLog?.Invoke($"Received: {data.Replace("\r", "\\r").Replace("\n", "\\n")}");
        }
        catch { }
    }

    public void Dispose()
    {
        Disconnect();
    }
#endif
}
