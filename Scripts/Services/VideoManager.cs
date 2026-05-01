using Godot;
using VRPlayerProject.Native;

namespace VRPlayerProject.Services;

public partial class VideoManager : Node
{
	private GoZenVideo? _video;
	private ShaderMaterial? _sphereMaterial;
	private ShaderMaterial? _flatMaterial;

	private ImageTexture? _yTexture;
	private ImageTexture? _uTexture;
	private ImageTexture? _vTexture;

	private AudioStreamPlayer? _audioPlayer;
	private AudioStreamFFmpeg? _audioStream;

	private bool _texturesDirty;

	public double DurationMs { get; private set; }
	public double PositionMs { get; private set; }
	public int VideoWidth { get; private set; }
	public int VideoHeight { get; private set; }

	public PlaybackState State { get; private set; } = PlaybackState.Idle;
	public VideoFormat CurrentFormat { get; private set; } = VideoFormat.Mono360;

	private double _speed = 1.0;
	private double _frameTime;
	private double _accumulator;
	private int _currentFrame;
	private int _frameCount;
	private double _seekTargetMs = -1;

	public event Action<double>? OnPositionChanged;
	public event Action<PlaybackState>? OnStateChanged;
	public event Action<string>? OnLog;
	public event Action<int, int>? OnDimensionsChanged;

	public override void _Ready()
	{
		GD.Print("[VideoManager] Initialized with gde_gozen.");
	}

	public void SetShaderMaterials(ShaderMaterial sphereMaterial, ShaderMaterial flatMaterial)
	{
		_sphereMaterial = sphereMaterial;
		_flatMaterial = flatMaterial;
	}

	public void SwitchFormat(VideoFormat format)
	{
		CurrentFormat = format;
		if (_sphereMaterial != null)
			_sphereMaterial.SetShaderParameter("projection_mode", (int)format);
	}

	public bool LoadFile(string path, VideoFormat format)
	{
		if (string.IsNullOrEmpty(path))
		{
			SetErrorState();
			return false;
		}

		CloseVideo();

		CurrentFormat = format;
		State = PlaybackState.Loading;
		OnStateChanged?.Invoke(State);

		string? resolvedPath = ResolvePathForPlayer(path);
		if (resolvedPath == null)
		{
			OnLog?.Invoke($"File not found: {path}");
			SetErrorState();
			return false;
		}

		_video = GoZenLib.CreateVideo();
		if (_video == null)
		{
			OnLog?.Invoke("GoZenVideo not available.");
			SetErrorState();
			return false;
		}

		if (_video.Open(resolvedPath) != (int)Error.Ok)
		{
			OnLog?.Invoke($"Cannot open video: {System.IO.Path.GetFileName(path)}");
			CloseVideo();
			SetErrorState();
			return false;
		}

		_frameCount = _video.GetFrameCount();
		float framerate = _video.GetFramerate();
		if (framerate <= 0) framerate = 30f;
		_frameTime = 1.0 / framerate;

		var res = _video.GetResolution();
		var actualRes = _video.GetActualResolution();
		VideoWidth = res.X;
		VideoHeight = res.Y;
		int actualW = actualRes.X;
		int actualH = actualRes.Y;

		GD.Print($"[VideoManager] Video: {VideoWidth}x{VideoHeight} (actual: {actualW}x{actualH}), {framerate}fps, {_frameCount} frames");

		OnDimensionsChanged?.Invoke(VideoWidth, VideoHeight);

		string colorProfile = _video.GetColorProfile();
		bool fullRange = _video.IsFullColorRange();
		var cp = GoZenLib.GetColorProfileVector(colorProfile);

		ApplyShaderSettings(cp, fullRange, new Vector2(actualW, actualH));

		_video.SeekFrame(0);
		_video.NextFrame();
		CreateTextures();
		UpdateTexturesFromVideo();
		UpdateShaderTextures();

		_currentFrame = 0;
		_accumulator = 0;
		PositionMs = 0;
		DurationMs = (_frameCount / (double)framerate) * 1000.0;

		SetupAudio(resolvedPath);

		State = PlaybackState.Paused;
		OnStateChanged?.Invoke(State);

		OnLog?.Invoke($"Loaded: {System.IO.Path.GetFileName(path)}");
		return true;
	}

	private void ApplyShaderSettings(Vector4 colorProfile, bool fullRange, Vector2 yuvRes)
	{
		foreach (var mat in new[] { _sphereMaterial, _flatMaterial })
		{
			if (mat == null) continue;
			mat.SetShaderParameter("color_profile", colorProfile);
			mat.SetShaderParameter("full_color_range", fullRange);
			mat.SetShaderParameter("yuv_resolution", yuvRes);
			mat.SetShaderParameter("projection_mode", (int)CurrentFormat);
		}
	}

	private void CreateTextures()
	{
		_yTexture?.Dispose();
		_uTexture?.Dispose();
		_vTexture?.Dispose();

		var yImg = _video!.GetYData();
		var uImg = _video.GetUData();
		var vImg = _video.GetVData();

		_yTexture = ImageTexture.CreateFromImage(yImg);
		_uTexture = ImageTexture.CreateFromImage(uImg);
		_vTexture = ImageTexture.CreateFromImage(vImg);
	}

	private void UpdateTexturesFromVideo()
	{
		if (_video == null || !_video.IsOpen()) return;

		_yTexture?.Update(_video.GetYData());
		_uTexture?.Update(_video.GetUData());
		_vTexture?.Update(_video.GetVData());
		_texturesDirty = true;
	}

	private void UpdateShaderTextures()
	{
		if (!_texturesDirty) return;

		foreach (var mat in new[] { _sphereMaterial, _flatMaterial })
		{
			if (mat == null) continue;
			mat.SetShaderParameter("y_data", _yTexture);
			mat.SetShaderParameter("u_data", _uTexture);
			mat.SetShaderParameter("v_data", _vTexture);
		}

		_texturesDirty = false;
	}

	private void SetupAudio(string resolvedPath)
	{
		_audioPlayer?.QueueFree();
		_audioStream = null;

		_audioStream = new AudioStreamFFmpeg();
		if (_audioStream.Open(resolvedPath) != (int)Error.Ok)
		{
			GD.PrintErr("[VideoManager] Failed to open audio stream.");
			_audioStream = null;
			return;
		}

		_audioPlayer = new AudioStreamPlayer();
		_audioPlayer.Stream = _audioStream;
		_audioPlayer.Bus = "Master";
		AddChild(_audioPlayer);
	}

	private void CloseVideo()
	{
		if (_video != null)
		{
			_video.Close();
			_video = null;
		}

		if (_audioPlayer != null)
		{
			_audioPlayer.Stop();
			_audioPlayer.QueueFree();
			_audioPlayer = null;
		}
		_audioStream = null;

		_yTexture?.Dispose();
		_uTexture?.Dispose();
		_vTexture?.Dispose();
		_yTexture = null;
		_uTexture = null;
		_vTexture = null;
		_texturesDirty = false;
	}

	private void SetErrorState()
	{
		State = PlaybackState.Error;
		OnStateChanged?.Invoke(State);
	}

	private static string? ResolvePathForPlayer(string path)
	{
		if (Godot.FileAccess.FileExists(path))
			return ProjectSettings.GlobalizePath(path);

		try
		{
			string cacheDir = ProjectSettings.GlobalizePath("user://video_cache/");
			System.IO.Directory.CreateDirectory(cacheDir);
			string dest = System.IO.Path.Combine(cacheDir, System.IO.Path.GetFileName(path));
			if (System.IO.File.Exists(dest)) return dest;
		}
		catch { }

		return null;
	}

	public void Play()
	{
		if (_video == null || !_video.IsOpen())
		{
			SetErrorState();
			return;
		}

		if (_audioPlayer != null && _audioPlayer.Stream != null)
		{
			_audioPlayer.Play((float)(_currentFrame * _frameTime));
		}

		State = PlaybackState.Playing;
		OnStateChanged?.Invoke(State);
	}

	public void Pause()
	{
		if (_audioPlayer != null)
			_audioPlayer.StreamPaused = true;

		State = PlaybackState.Paused;
		OnStateChanged?.Invoke(State);
	}

	public void Resume()
	{
		if (_video == null || !_video.IsOpen()) return;

		if (_audioPlayer != null)
			_audioPlayer.StreamPaused = false;

		State = PlaybackState.Playing;
		OnStateChanged?.Invoke(State);
	}

	public void TogglePlayPause()
	{
		if (State == PlaybackState.Playing) Pause();
		else Resume();
	}

	public void Stop()
	{
		if (_audioPlayer != null)
			_audioPlayer.Stop();

		State = PlaybackState.Idle;
		OnStateChanged?.Invoke(State);
	}

	public void SeekTo(double ms)
	{
		if (_video == null || !_video.IsOpen() || _frameCount <= 0) return;

		double framerate = 1.0 / _frameTime;
		int targetFrame = (int)(ms / 1000.0 * framerate);
		targetFrame = Mathf.Clamp(targetFrame, 0, _frameCount - 1);

		_seekTargetMs = ms;
		_currentFrame = targetFrame;
		_accumulator = 0;

		_video.SeekFrame(targetFrame);
		_video.NextFrame();
		UpdateTexturesFromVideo();

		if (_audioPlayer != null && _audioPlayer.Stream != null)
		{
			_audioPlayer.Play((float)(_currentFrame * _frameTime));
			_audioPlayer.StreamPaused = State != PlaybackState.Playing;
		}

		PositionMs = (_currentFrame / framerate) * 1000.0;
		OnPositionChanged?.Invoke(PositionMs);
	}

	public void SetSpeed(double speed)
	{
		_speed = Mathf.Clamp(speed, 0.25, 4.0);

		if (_audioPlayer != null)
			_audioPlayer.PitchScale = (float)_speed;
	}

	public override void _Process(double delta)
	{
		if (_video == null || !_video.IsOpen()) return;

		if (State == PlaybackState.Playing)
		{
			_accumulator += delta * _speed;

			while (_accumulator >= _frameTime && _currentFrame < _frameCount - 1)
			{
				_accumulator -= _frameTime;
				_currentFrame++;
				_video.NextFrame();
			}

			if (_currentFrame >= _frameCount - 1)
			{
				State = PlaybackState.Ended;
				OnStateChanged?.Invoke(State);
				if (_audioPlayer != null)
					_audioPlayer.StreamPaused = true;
			}

			UpdateTexturesFromVideo();

			double framerate = 1.0 / _frameTime;
			PositionMs = (_currentFrame / framerate) * 1000.0;
			OnPositionChanged?.Invoke(PositionMs);

			SyncAudio();
		}

		UpdateShaderTextures();
	}

	private void SyncAudio()
	{
		if (_audioPlayer == null || _audioPlayer.Stream == null)
			return;

		if (!_audioPlayer.Playing)
		{
			_audioPlayer.Play((float)(_currentFrame * _frameTime));
			_audioPlayer.PitchScale = (float)_speed;
		}

		double expectedSec = _currentFrame * _frameTime;
		double actualSec = _audioPlayer.GetPlaybackPosition() +
						   AudioServer.GetTimeSinceLastMix();
		double offset = actualSec - expectedSec;

		if (Mathf.Abs(offset) > 0.1)
		{
			_audioPlayer.Play((float)expectedSec);
			_audioPlayer.PitchScale = (float)_speed;
		}
	}

	public override void _ExitTree()
	{
		CloseVideo();
	}
}
