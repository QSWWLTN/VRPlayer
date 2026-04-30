using Godot;
using VRPlayerProject.Services;

namespace VRPlayerProject;

public partial class Main : Control
{
    private LineEdit? _videoPathEdit;
    private LineEdit? _scriptPathEdit;
    private OptionButton? _formatDropdown;
    private Button? _browseVideoBtn;
    private Button? _browseScriptBtn;
    private Button? _launchVrBtn;
    private RichTextLabel? _logLabel;

    private OptionButton? _serialPortDropdown;
    private Button? _serialRefreshBtn;
    private LineEdit? _serialBaudEdit;
    private Button? _serialBtn;
    private Label? _serialStatus;

    private LineEdit? _wsHostEdit;
    private LineEdit? _wsPortEdit;
    private Button? _wsBtn;
    private Label? _wsStatus;

    private Control? _serialSection;

    private string? _selectedVideoPath;
    private string? _selectedScriptPath;
    private int _selectedFormat = 3;

    private ScriptMatcherService? _scriptMatcher;
    private SerialOutputService? _serialService;
    private WebSocketOutputService? _wsService;

    private static readonly string[] FormatNames =
        { "Flat", "180°", "Fisheye 180°", "360° Mono", "360° Stereo" };

    private static bool IsAndroid => OS.HasFeature("android");

    public override void _Ready()
    {
        _videoPathEdit = GetNodeOrNull<LineEdit>("Panel/VBoxContainer/VideoSection/VideoPath");
        _scriptPathEdit = GetNodeOrNull<LineEdit>("Panel/VBoxContainer/ScriptSection/ScriptPath");
        _formatDropdown = GetNodeOrNull<OptionButton>("Panel/VBoxContainer/FormatSection/FormatDropdown");
        _browseVideoBtn = GetNodeOrNull<Button>("Panel/VBoxContainer/VideoSection/BrowseVideoBtn");
        _browseScriptBtn = GetNodeOrNull<Button>("Panel/VBoxContainer/ScriptSection/BrowseScriptBtn");
        _launchVrBtn = GetNodeOrNull<Button>("Panel/VBoxContainer/ActionSection/LaunchVRBtn");
        _logLabel = GetNodeOrNull<RichTextLabel>("Panel/VBoxContainer/LogSection/LogScroll/LogLabel");

        _serialPortDropdown = GetNodeOrNull<OptionButton>("Panel/VBoxContainer/SerialSection/SerialPort");
        _serialRefreshBtn = GetNodeOrNull<Button>("Panel/VBoxContainer/SerialSection/SerialRefreshBtn");
        _serialBaudEdit = GetNodeOrNull<LineEdit>("Panel/VBoxContainer/SerialSection/SerialBaud");
        _serialBtn = GetNodeOrNull<Button>("Panel/VBoxContainer/SerialSection/SerialBtn");
        _serialStatus = GetNodeOrNull<Label>("Panel/VBoxContainer/SerialSection/SerialStatus");
        _serialSection = GetNodeOrNull<Control>("Panel/VBoxContainer/SerialSection");

        _wsHostEdit = GetNodeOrNull<LineEdit>("Panel/VBoxContainer/WsSection/WsHost");
        _wsPortEdit = GetNodeOrNull<LineEdit>("Panel/VBoxContainer/WsSection/WsPort");
        _wsBtn = GetNodeOrNull<Button>("Panel/VBoxContainer/WsSection/WsBtn");
        _wsStatus = GetNodeOrNull<Label>("Panel/VBoxContainer/WsSection/WsStatus");

        _scriptMatcher = new ScriptMatcherService();
        _serialService = new SerialOutputService();
        _wsService = new WebSocketOutputService();

        if (_formatDropdown != null)
        {
            for (int i = 0; i < FormatNames.Length; i++)
                _formatDropdown.AddItem(FormatNames[i], i);
            _formatDropdown.Select(3);
            _formatDropdown.ItemSelected += (index) => _selectedFormat = (int)index;
        }

        if (IsAndroid && _serialSection != null)
            _serialSection.Visible = false;

        RefreshSerialPorts();

        SubscribeUI();

        _serialService.OnConnectionChanged += (connected) =>
            UpdateSerialStatus(connected ? _serialService.ConnectedPortName ?? "" : "");

        _wsService.OnConnectionChanged += (connected) =>
        {
            if (_wsService.IsConnecting) return;
            UpdateWsStatus(connected ? $"{_wsService.Host}:{_wsService.Port}" : "");
        };

        _wsService.OnLog += (msg) => Log($"[WS] {msg}");

        Log("VR Player ready. Connect output, select a video, and launch.");
    }

    private void RefreshSerialPorts()
    {
        if (_serialPortDropdown == null || _serialService == null) return;

        _serialPortDropdown.Clear();
        var ports = _serialService.GetAvailablePorts();
        if (ports.Length == 0)
        {
            _serialPortDropdown.AddItem("(no ports found)");
            _serialPortDropdown.Disabled = true;
        }
        else
        {
            foreach (var p in ports)
                _serialPortDropdown.AddItem(p);
            _serialPortDropdown.Select(0);
            _serialPortDropdown.Disabled = false;
        }
    }

    private void SubscribeUI()
    {
        if (_browseVideoBtn != null) _browseVideoBtn.Pressed += OnBrowseVideo;
        if (_browseScriptBtn != null) _browseScriptBtn.Pressed += OnBrowseScript;
        if (_launchVrBtn != null) _launchVrBtn.Pressed += OnLaunchVR;

        if (_serialRefreshBtn != null)
            _serialRefreshBtn.Pressed += RefreshSerialPorts;

        if (_serialBtn != null)
        {
            _serialBtn.Pressed += () =>
            {
                if (_serialService!.IsConnected)
                {
                    _serialService.Disconnect();
                    UpdateSerialStatus("");
                }
                else
                {
                    string port = _serialPortDropdown?.GetItemText(_serialPortDropdown.Selected) ?? "";
                    if (string.IsNullOrEmpty(port) || port.StartsWith("(")) return;
                    int baud = int.TryParse(_serialBaudEdit?.Text, out var b) ? b : 115200;
                    _serialService.Connect(port, baud);
                }
            };
        }

        if (_wsBtn != null)
        {
            _wsBtn.Pressed += async () =>
            {
                if (_wsService!.IsConnected || _wsService.IsConnecting)
                {
                    _wsService.Disconnect();
                    UpdateWsStatus("");
                    return;
                }

                _wsBtn.Text = "Connecting...";
                _wsBtn.Disabled = true;

                try
                {
                    string host = _wsHostEdit?.Text ?? "192.168.1.100";
                    int port = int.TryParse(_wsPortEdit?.Text, out var p) ? p : 80;
                    _wsService.SetHost(host);
                    _wsService.SetPort(port);
                    await _wsService.Connect();
                }
                finally
                {
                    _wsBtn.Disabled = false;
                    if (!_wsService.IsConnected)
                    {
                        _wsBtn.Text = "Connect";
                        UpdateWsStatus("");
                    }
                }
            };
        }
    }

    private void UpdateSerialStatus(string info)
    {
        bool ok = _serialService?.IsConnected ?? false;
        if (_serialStatus != null)
        {
            _serialStatus.Text = ok ? $"● {info}" : "○";
            _serialStatus.Modulate = ok ? Colors.Green : Colors.Gray;
        }
        if (_serialBtn != null) _serialBtn.Text = ok ? "Disconnect" : "Connect";
    }

    private void UpdateWsStatus(string info)
    {
        bool ok = _wsService?.IsConnected ?? false;
        if (_wsStatus != null)
        {
            _wsStatus.Text = ok ? $"● {info}" : "○";
            _wsStatus.Modulate = ok ? Colors.Green : Colors.Gray;
        }
        if (_wsBtn != null) _wsBtn.Text = ok ? "Disconnect" : "Connect";
    }

    private void OnBrowseVideo()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = IsAndroid ? FileDialog.AccessEnum.Userdata : FileDialog.AccessEnum.Filesystem,
            Title = "Select Video File",
            Filters = new[] { "*.mp4, *.mkv, *.avi, *.mov, *.webm ; Video Files", "*.* ; All Files" }
        };

        AddChild(dialog);
        dialog.FileSelected += (path) =>
        {
            _selectedVideoPath = path;
            _videoPathEdit!.Text = path;
            AutoMatchScript(path);
            dialog.QueueFree();
        };
        dialog.PopupCentered(new Vector2I(700, 500));
    }

    private void AutoMatchScript(string videoPath)
    {
        if (_scriptMatcher == null) return;
        var scriptPath = _scriptMatcher.FindScript(videoPath);
        if (scriptPath != null)
        {
            _selectedScriptPath = scriptPath;
            _scriptPathEdit!.Text = scriptPath;
            Log($"Auto-matched script: {scriptPath.GetFile()}");
        }
        else
        {
            _scriptPathEdit!.Text = "";
            Log("No matching .funscript found.");
        }
    }

    private void OnBrowseScript()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = IsAndroid ? FileDialog.AccessEnum.Userdata : FileDialog.AccessEnum.Filesystem,
            Title = "Select Funscript File",
            Filters = new[] { "*.funscript, *.json ; Funscript Files", "*.* ; All Files" }
        };

        AddChild(dialog);
        dialog.FileSelected += (path) =>
        {
            _selectedScriptPath = path;
            _scriptPathEdit!.Text = path;
            Log($"Script selected: {path.GetFile()}");
            dialog.QueueFree();
        };
        dialog.PopupCentered(new Vector2I(700, 500));
    }

    private void OnLaunchVR()
    {
        if (string.IsNullOrEmpty(_selectedVideoPath))
        {
            Log("Error: No video selected!");
            return;
        }

        Log($"Launching VR player ({FormatNames[_selectedFormat]})...");
        Log($"Video: {_selectedVideoPath}");
        if (!string.IsNullOrEmpty(_selectedScriptPath))
            Log($"Script: {_selectedScriptPath}");

        var vrScene = ResourceLoader.Load<PackedScene>("res://Scenes/VRPlayer.tscn");
        if (vrScene == null) return;

        var vr = vrScene.Instantiate<VRPlayer>();
        vr.Initialize(_selectedVideoPath, _selectedScriptPath, (VideoFormat)_selectedFormat, _serialService, _wsService);
        GetTree().Root.AddChild(vr);
        GetTree().CurrentScene = vr;
    }

    private void Log(string message)
    {
        if (_logLabel == null) return;
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        _logLabel.Text += $"\n[{timestamp}] {message}";
        _logLabel.ScrollToLine(int.MaxValue);
    }
}
