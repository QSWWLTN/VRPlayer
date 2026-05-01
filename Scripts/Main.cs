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
		{ "Flat", "180°", "Fisheye 180°", "360° Mono", "360° Stereo", "180° Stereo" };

	private static bool IsAndroid => OS.HasFeature("android");

	public override void _Ready()
	{
		// 1. 强制唤醒 OpenXR
		var xrInterface = XRServer.FindInterface("OpenXR");
		if (xrInterface != null)
		{
			if (!xrInterface.IsInitialized()) xrInterface.Initialize();
			if (xrInterface.IsInitialized()) 
			{
				GetTree().Root.UseXR = true;
				
				// 修复：重置玩家朝向。
				// 使用一个小延迟，确保头显的追踪数据已经加载完毕，然后将玩家在现实中的正前方强制对齐到游戏UI所在的 -Z 轴。
				GetTree().CreateTimer(0.1f).Timeout += () => 
				{
					XRServer.CenterOnHmd(XRServer.RotationMode.ResetButKeepTilt, true);
				};
			}
		}

		var subViewport = GetParent() as SubViewport; 
		var uiMesh = subViewport?.GetParent().GetNodeOrNull<MeshInstance3D>("UIMesh"); 

		if (uiMesh != null && subViewport != null)
		{
			// 修复：UI 太小的问题
			uiMesh.Scale = new Vector3(2.5f, 2.5f, 2.5f); // 将 UI 整体放大 2.5 倍
			uiMesh.Position = new Vector3(0, 1.5f, -3.0f); // 放大后稍微推远一点 (Z从-2改到-3)，避免贴脸

			// 动态创建一个不受光照影响的材质
			var mat = new StandardMaterial3D();
			mat.ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded; 
			mat.AlbedoTexture = subViewport.GetTexture(); 
			uiMesh.MaterialOverride = mat; 
		}
		
		if (IsAndroid)
		{
			OS.RequestPermissions();
		}
		
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
			Access = FileDialog.AccessEnum.Filesystem, // 修复：放弃 Userdata，使用全局文件系统
			Title = "Select Video File",
			Filters = new[] { "*.mp4, *.mkv, *.avi, *.mov, *.webm ; Video Files", "*.* ; All Files" }
		};

		if (IsAndroid)
		{
			// 修复：强制将起始路径设为安卓头显的内部存储根目录
			dialog.CurrentDir = "/storage/emulated/0/"; 
		}

		AddChild(dialog);
		dialog.FileSelected += (path) =>
		{
			_selectedVideoPath = NormalizePath(path);
			_videoPathEdit!.Text = _selectedVideoPath;
			AutoMatchScript(_selectedVideoPath);
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
			Access = FileDialog.AccessEnum.Filesystem, // 修复：使用全局文件系统
			Title = "Select Funscript File",
			Filters = new[] { "*.funscript, *.json ; Funscript Files", "*.* ; All Files" }
		};

		if (IsAndroid)
		{
			dialog.CurrentDir = "/storage/emulated/0/"; 
		}

		AddChild(dialog);
		dialog.FileSelected += (path) =>
		{
			_selectedScriptPath = NormalizePath(path);
			_scriptPathEdit!.Text = _selectedScriptPath;
			Log($"Script selected: {_selectedScriptPath.GetFile()}");
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

		HideParentVrPanel();
	}

	private void HideParentVrPanel()
	{
		Node? parent = this;
		while (parent != null && parent != GetTree().Root)
		{
			if (parent is UI.VrPanel3D panel)
			{
				panel.Visible = false;
				panel.FollowCamera = false;
				return;
			}
			parent = parent.GetParent();
		}
	}

	private static string NormalizePath(string path)
	{
		if (string.IsNullOrEmpty(path))
			return path;

		path = path.Replace("\\", "/");

		if (!IsAndroid)
			return path;

		if (!path.StartsWith("/"))
		{
			if (path.StartsWith("res://") || path.StartsWith("user://"))
				path = ProjectSettings.GlobalizePath(path);
		}

		return path;
	}

	private void Log(string message)
	{
		if (_logLabel == null) return;
		var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
		_logLabel.Text += $"\n[{timestamp}] {message}";
		_logLabel.ScrollToLine(int.MaxValue);
	}
}
