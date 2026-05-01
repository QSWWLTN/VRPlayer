using Godot;
using VRPlayerProject.Services;
using System;

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
	
	// VR 专用虚拟键盘组件
	private Panel? _vrKeyboard;
	private LineEdit? _activeLineEdit;

	private static readonly string[] FormatNames =
		{ "Flat", "180°", "Fisheye 180°", "360° Mono", "360° Stereo", "180° Stereo" };

	private static bool IsAndroid => OS.HasFeature("android");

	public override void _Ready()
	{
		if (IsAndroid)
			OS.RequestPermissions();

		var xrInterface = XRServer.FindInterface("OpenXR");
		if (xrInterface != null)
		{
			if (!xrInterface.IsInitialized()) xrInterface.Initialize();
			if (xrInterface.IsInitialized())
			{
				GetTree().Root.UseXR = true;
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
			uiMesh.Scale = new Vector3(2.5f, 2.5f, 2.5f);
			uiMesh.Position = new Vector3(0, 1.5f, -3.0f);

			var mat = new StandardMaterial3D();
			mat.ShadingMode = StandardMaterial3D.ShadingModeEnum.Unshaded;
			mat.AlbedoTexture = subViewport.GetTexture();
			uiMesh.MaterialOverride = mat;
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
		
		// 生成并绑定游戏内 VR 键盘
		SetupVirtualKeyboard();

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

	// --- 新增：彻底解决 VR 输入问题的内置 UI 键盘 ---
	private void SetupVirtualKeyboard()
	{
		// 1. 构建游戏内 UI 键盘面板
		_vrKeyboard = new Panel();
		_vrKeyboard.Size = new Vector2(1000, 380);
		_vrKeyboard.Position = new Vector2(140, 400); // 在 1280x800 的屏幕中居中靠下
		_vrKeyboard.Visible = false;

		var styleBox = new StyleBoxFlat 
		{ 
			BgColor = new Color(0.15f, 0.15f, 0.15f, 0.98f), 
			CornerRadiusTopLeft = 15, 
			CornerRadiusTopRight = 15,
			CornerRadiusBottomLeft = 15,
			CornerRadiusBottomRight = 15
		};
		_vrKeyboard.AddThemeStyleboxOverride("panel", styleBox);

		var vbox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		vbox.SetAnchorsPreset(LayoutPreset.FullRect);
		_vrKeyboard.AddChild(vbox);

		var closeBtn = new Button { Text = "▼ 完成 / 收起键盘 ▼", CustomMinimumSize = new Vector2(300, 45) };
		closeBtn.AddThemeFontSizeOverride("font_size", 20);
		closeBtn.Pressed += () => _vrKeyboard.Visible = false;
		vbox.AddChild(closeBtn);

		// 键盘按键布局（包含字母、数字和常用网络符号）
		string[] rows = {
			"1 2 3 4 5 6 7 8 9 0 - _",
			"q w e r t y u i o p",
			"a s d f g h j k l : /",
            "z x c v b n m . : //"
		};

		foreach (var row in rows)
		{
			var hbox = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			foreach (var key in row.Split(' '))
			{
				var btn = new Button { Text = key, CustomMinimumSize = new Vector2(65, 55) };
				btn.AddThemeFontSizeOverride("font_size", 24);
				btn.Pressed += () => {
					if (_activeLineEdit != null)
					{
						_activeLineEdit.Text += key;
						_activeLineEdit.CaretColumn = _activeLineEdit.Text.Length; // 保持光标在最后
					}
				};
				hbox.AddChild(btn);
			}
			vbox.AddChild(hbox);
		}

		// 功能键行 (空格、退格、清空)
		var actionRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		var spaceBtn = new Button { Text = "Space", CustomMinimumSize = new Vector2(250, 55) };
		spaceBtn.AddThemeFontSizeOverride("font_size", 24);
		spaceBtn.Pressed += () => { if (_activeLineEdit != null) _activeLineEdit.Text += " "; };
		
		var bsBtn = new Button { Text = "Backspace", CustomMinimumSize = new Vector2(150, 55) };
		bsBtn.AddThemeFontSizeOverride("font_size", 24);
		bsBtn.Pressed += () => { 
			if (_activeLineEdit != null && _activeLineEdit.Text.Length > 0) 
				_activeLineEdit.Text = _activeLineEdit.Text.Substring(0, _activeLineEdit.Text.Length - 1); 
		};

		var clearBtn = new Button { Text = "Clear", CustomMinimumSize = new Vector2(100, 55) };
		clearBtn.AddThemeFontSizeOverride("font_size", 24);
		clearBtn.Pressed += () => { if (_activeLineEdit != null) _activeLineEdit.Text = ""; };

		actionRow.AddChild(spaceBtn);
		actionRow.AddChild(bsBtn);
		actionRow.AddChild(clearBtn);
		vbox.AddChild(actionRow);

		// 将键盘添加到主面板中
		var rootPanel = GetNodeOrNull<Control>("Panel");
		if (rootPanel != null) rootPanel.AddChild(_vrKeyboard);
		else AddChild(_vrKeyboard);

		// 2. 自动搜索并绑定所有输入框
		var lineEdits = new Godot.Collections.Array<LineEdit>();
		FindAllLineEdits(this, lineEdits);

		foreach (var le in lineEdits)
		{
			// 当射线点中任何一个输入框时，展开这个内置键盘
			le.FocusEntered += () =>
			{
				_activeLineEdit = le;
				if (_vrKeyboard != null)
				{
					_vrKeyboard.Visible = true;
					_vrKeyboard.MoveToFront(); // 确保覆盖在所有界面之上
				}
			};
		}
	}

	// 递归查找 UI 树下所有的 LineEdit 组件
	private void FindAllLineEdits(Node node, Godot.Collections.Array<LineEdit> result)
	{
		if (node is LineEdit le) result.Add(le);
		foreach (var child in node.GetChildren())
		{
			FindAllLineEdits(child, result);
		}
	}
	// --------------------------------------------------------

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
			Access = FileDialog.AccessEnum.Filesystem,
			Title = "Select Video File",
			Filters = new[] { "*.mp4, *.mkv, *.avi, *.mov, *.webm ; Video Files", "*.* ; All Files" }
		};

		if (IsAndroid)
			dialog.CurrentDir = "/storage/emulated/0/";

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
			Access = FileDialog.AccessEnum.Filesystem,
			Title = "Select Funscript File",
			Filters = new[] { "*.funscript, *.json ; Funscript Files", "*.* ; All Files" }
		};

		if (IsAndroid)
			dialog.CurrentDir = "/storage/emulated/0/";

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

		var mainScene = GetTree().CurrentScene;
		GetTree().Root.AddChild(vr);
		GetTree().CurrentScene = vr;
		mainScene?.QueueFree();
	}

	private void Log(string message)
	{
		if (_logLabel == null) return;
		var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
		_logLabel.Text += $"\n[{timestamp}] {message}";
		_logLabel.ScrollToLine(int.MaxValue);
	}
}
