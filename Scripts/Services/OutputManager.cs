using VRPlayerProject.Models;

namespace VRPlayerProject.Services;

public class OutputManager
{
    private readonly SerialOutputService? _serialService;
    private readonly WebSocketOutputService? _webSocketService;
    private int _maxPercentage = 100;

    public bool HasOutput => (_serialService?.IsConnected ?? false) ||
                             (_webSocketService?.IsConnected ?? false);

    public OutputManager(SerialOutputService? serial, WebSocketOutputService? ws)
    {
        _serialService = serial;
        _webSocketService = ws;
    }

    public void SetMaxPercentage(int value) => _maxPercentage = value;

    public void SendPosition(double percentage, ProtocolType protocol = ProtocolType.TCode)
    {
        _serialService?.SendPosition(percentage, _maxPercentage, protocol);
        _webSocketService?.SendPosition(percentage, _maxPercentage);
    }

    public void Dispose()
    {
        _serialService?.Dispose();
        _webSocketService?.Dispose();
    }
}
