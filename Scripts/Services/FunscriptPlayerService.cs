using VRPlayerProject.Models;

namespace VRPlayerProject.Services;

public class FunscriptPlayerService
{
    private List<FunscriptAction>? _actions;
    private int _currentActionIndex;
    private double _speedFactor = 1.0;
    private int _maxPercentage = 100;
    private bool _interpolationEnabled;
    private bool _loopEnabled;
    private double _lastSentPosition = -1;
    private double _lastSyncTimeMs = -1;

    public double TotalDurationMs { get; private set; }
    public double CurrentPositionMs { get; private set; }
    public List<FunscriptAction>? Actions => _actions;
    public int MaxPercentage => _maxPercentage;
    public double SpeedFactor => _speedFactor;
    public bool InterpolationEnabled => _interpolationEnabled;
    public bool LoopEnabled => _loopEnabled;

    public event Action<double>? OnPositionUpdate;
    public event Action<double>? OnOutputPosition;

    public void SetActions(List<FunscriptAction>? actions)
    {
        _actions = actions;
        _currentActionIndex = 0;
        CurrentPositionMs = 0;
        _lastSentPosition = -1;
        _lastSyncTimeMs = -1;
        TotalDurationMs = actions is { Count: > 0 } ? actions[^1].At : 0;
    }

    public void SetSpeedFactor(double factor) => _speedFactor = factor;
    public void SetMaxPercentage(int value) => _maxPercentage = value;
    public void SetInterpolationEnabled(bool enabled) => _interpolationEnabled = enabled;
    public void SetLoopEnabled(bool enabled) => _loopEnabled = enabled;

    public double SyncToPosition(double videoTimeMs)
    {
        if (_actions == null || _actions.Count == 0) return -1;

        CurrentPositionMs = videoTimeMs;

        // Detect seek backward: if time jumped backward, reset cursor
        if (videoTimeMs < _lastSyncTimeMs && !_interpolationEnabled)
        {
            _currentActionIndex = 0;
            _lastSentPosition = -1;
        }
        _lastSyncTimeMs = videoTimeMs;

        if (_interpolationEnabled)
        {
            int prevIdx = -1, nextIdx = -1;
            for (int i = 0; i < _actions.Count; i++)
            {
                if (_actions[i].At <= videoTimeMs) prevIdx = i;
                else { nextIdx = i; break; }
            }

            double position;
            if (prevIdx == -1) position = 0;
            else if (nextIdx == -1) position = _actions[prevIdx].Pos;
            else
            {
                var prev = _actions[prevIdx];
                var next = _actions[nextIdx];
                double t = (videoTimeMs - prev.At) / (double)(next.At - prev.At);
                position = prev.Pos + (next.Pos - prev.Pos) * t;
            }

            if (Math.Abs(_lastSentPosition - position) >= 0.1)
            {
                _lastSentPosition = position;
                OnOutputPosition?.Invoke(position);
            }
        }
        else
        {
            while (_currentActionIndex < _actions.Count &&
                   _actions[_currentActionIndex].At <= videoTimeMs)
            {
                var action = _actions[_currentActionIndex];
                OnOutputPosition?.Invoke(action.Pos);
                _currentActionIndex++;
            }
        }

        OnPositionUpdate?.Invoke(videoTimeMs);
        return _lastSentPosition;
    }

    public void SeekTo(double positionMs)
    {
        if (_actions == null) return;

        CurrentPositionMs = positionMs;
        _lastSentPosition = -1;
        _currentActionIndex = 0;
        _lastSyncTimeMs = positionMs;

        while (_currentActionIndex < _actions.Count &&
               _actions[_currentActionIndex].At <= positionMs)
        {
            _currentActionIndex++;
        }

        // Fire the correct position at the seek point so the device updates
        double outputPos;
        if (_actions.Count == 0) outputPos = 0;
        else if (_currentActionIndex == 0) outputPos = 0;
        else if (_interpolationEnabled)
        {
            int prevIdx = _currentActionIndex - 1;
            if (prevIdx < 0) outputPos = 0;
            else if (_currentActionIndex >= _actions.Count)
                outputPos = _actions[prevIdx].Pos;
            else
            {
                var prev = _actions[prevIdx];
                var next = _actions[_currentActionIndex];
                double t = (positionMs - prev.At) / (double)(next.At - prev.At);
                outputPos = prev.Pos + (next.Pos - prev.Pos) * t;
            }
        }
        else
        {
            outputPos = _currentActionIndex > 0
                ? _actions[_currentActionIndex - 1].Pos
                : 0;
        }

        _lastSentPosition = outputPos;
        OnOutputPosition?.Invoke(outputPos);
        OnPositionUpdate?.Invoke(positionMs);
    }
}
