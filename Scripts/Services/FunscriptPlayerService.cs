using VRPlayerProject.Models;
using System;
using System.Collections.Generic;

namespace VRPlayerProject.Services;

public class FunscriptPlayerService
{
    private List<FunscriptAction>? _originalActions;
    private List<FunscriptAction>? _actions;
    private int _currentActionIndex;
    private double _speedFactor = 1.0;
    private int _maxPercentage = 100;
    private bool _interpolationEnabled = true;
    private bool _loopEnabled;
    private double _lastSentPosition = -1;
    private double _lastSyncTimeMs = -1;

    // --- 过冲运动曲线参数 ---
    private bool _overshootEnabled = false;
    private int _overshootDropThreshold = 30;     
    private int _overshootTimeThresholdMs = 150;  
    private int _overshootAmount = 15;            
    private int _overshootRecoverTimeMs = 60;     

    public double TotalDurationMs { get; private set; }
    public double CurrentPositionMs { get; private set; }
    public List<FunscriptAction>? Actions => _actions;
    public int MaxPercentage => _maxPercentage;
    public double SpeedFactor => _speedFactor;
    public bool InterpolationEnabled => _interpolationEnabled;
    public bool LoopEnabled => _loopEnabled;

    public event Action<double>? OnPositionUpdate;
    public event Action<double>? OnOutputPosition;

    public void SetOvershootEnabled(bool enabled)
    {
        if (_overshootEnabled == enabled) return;
        _overshootEnabled = enabled;
        ProcessActions(); 
    }

    public void SetActions(List<FunscriptAction>? actions)
    {
        _originalActions = actions != null ? new List<FunscriptAction>(actions) : null;
        ProcessActions();
    }

    private void ProcessActions()
    {
        if (_originalActions == null)
        {
            _actions = null;
            _currentActionIndex = 0;
            CurrentPositionMs = 0;
            _lastSentPosition = -1;
            _lastSyncTimeMs = -1;
            TotalDurationMs = 0;
            return;
        }

        if (!_overshootEnabled)
        {
            _actions = new List<FunscriptAction>(_originalActions);
        }
        else
        {
            _actions = new List<FunscriptAction>();
            for (int i = 0; i < _originalActions.Count; i++)
            {
                var curr = _originalActions[i];
                if (i > 0)
                {
                    var prev = _originalActions[i - 1];
                    int drop = prev.Pos - curr.Pos;
                    int timeDiff = curr.At - prev.At;

                    if (drop >= _overshootDropThreshold && timeDiff >= _overshootTimeThresholdMs)
                    {
                        int overPos = Math.Max(0, curr.Pos - _overshootAmount);
                        _actions.Add(new FunscriptAction { At = curr.At, Pos = overPos });

                        int recoverTime = curr.At + _overshootRecoverTimeMs;
                        if (i < _originalActions.Count - 1)
                        {
                            int nextTime = _originalActions[i + 1].At;
                            if (recoverTime >= nextTime)
                            {
                                recoverTime = curr.At + (nextTime - curr.At) / 2;
                            }
                        }
                        _actions.Add(new FunscriptAction { At = recoverTime, Pos = curr.Pos });
                        continue;
                    }
                }
                _actions.Add(new FunscriptAction { At = curr.At, Pos = curr.Pos });
            }
        }

        _currentActionIndex = 0;
        CurrentPositionMs = 0;
        _lastSentPosition = -1;
        _lastSyncTimeMs = -1;
        TotalDurationMs = _actions is { Count: > 0 } ? _actions[^1].At : 0;
    }

    public void SetSpeedFactor(double factor) => _speedFactor = factor;
    public void SetMaxPercentage(int value) => _maxPercentage = value;
    public void SetInterpolationEnabled(bool enabled) => _interpolationEnabled = enabled;
    public void SetLoopEnabled(bool enabled) => _loopEnabled = enabled;

    public double SyncToPosition(double videoTimeMs)
    {
        if (_actions == null || _actions.Count == 0) return -1;

        CurrentPositionMs = videoTimeMs;

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
                if (_actions[i].At <= videoTimeMs)
                {
                    prevIdx = i;
                }
                else
                {
                    nextIdx = i;
                    break;
                }
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

            if (Math.Abs(_lastSentPosition - position) >= 0.5)
            {
                _lastSentPosition = position;
                OnOutputPosition?.Invoke(position);
            }
        }
        else
        {
            while (_currentActionIndex < _actions.Count && _actions[_currentActionIndex].At <= videoTimeMs)
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

        while (_currentActionIndex < _actions.Count && _actions[_currentActionIndex].At <= positionMs)
        {
            _currentActionIndex++;
        }

        double outputPos;
        if (_actions.Count == 0) outputPos = 0;
        else if (_currentActionIndex == 0) outputPos = 0;
        else if (_interpolationEnabled)
        {
            int prevIdx = _currentActionIndex - 1;
            if (prevIdx < 0) outputPos = 0;
            else if (_currentActionIndex >= _actions.Count) outputPos = _actions[prevIdx].Pos;
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
            outputPos = _currentActionIndex > 0 ? _actions[_currentActionIndex - 1].Pos : 0;
        }

        _lastSentPosition = outputPos;
        OnOutputPosition?.Invoke(outputPos);
        OnPositionUpdate?.Invoke(positionMs);
    }
}