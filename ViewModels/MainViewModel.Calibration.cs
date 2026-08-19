using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using MicForge.Audio;
using NAudio.CoreAudioApi;

namespace MicForge.ViewModels;

/// <summary>Gate noise-floor learning and the two-step (quiet, then talk) auto-calibration wizard.</summary>
public sealed partial class MainViewModel
{
    // Gate noise-floor learning.
    private bool _learning;
    private DateTime _learnEnd;
    private double _learnMaxDb;

    private void LearnNoise()
    {
        if (!(_engine.Running || _engine.Reconnecting))
        {
            MessageBox.Show("Start processing first, then stay quiet for a moment while MicForge samples the room.",
                "Learn noise floor");
            return;
        }
        _learnMaxDb = -120;
        _learnEnd = DateTime.UtcNow.AddMilliseconds(1200);
        _learning = true;
    }

    // Two-step auto-calibration: measure the room, then measure normal speech.
    private enum CalPhase { None, Quiet, Talk }
    private CalPhase _cal = CalPhase.None;
    private DateTime _calEnd;
    private double _calNoiseMax, _calNoiseFloor, _calSpeechSum;
    private int _calSpeechCount;

    private void Calibrate()
    {
        if (!(_engine.Running || _engine.Reconnecting))
        {
            MessageBox.Show("Start processing first. Then the wizard asks you to stay quiet for a moment, then talk normally.",
                "Auto-calibrate");
            return;
        }
        _cal = CalPhase.Quiet;
        _calNoiseMax = -120;
        _calEnd = DateTime.UtcNow.AddMilliseconds(1500);
    }

    private void UpdateCalibration(double ip, DateTime now)
    {
        if (_cal == CalPhase.None) return;
        double db = ip <= 0.00001 ? -120 : 20 * Math.Log10(ip);

        if (_cal == CalPhase.Quiet)
        {
            if (db > _calNoiseMax) _calNoiseMax = db;
            StatusText = "Calibrating — stay quiet…";
            if (now >= _calEnd)
            {
                _calNoiseFloor = _calNoiseMax;
                _calSpeechSum = 0; _calSpeechCount = 0;
                _cal = CalPhase.Talk;
                _calEnd = now.AddMilliseconds(2800);
            }
        }
        else
        {
            if (db > -40) { _calSpeechSum += db; _calSpeechCount++; }
            StatusText = "Calibrating — talk normally…";
            if (now >= _calEnd)
            {
                _cal = CalPhase.None;
                ApplyCalibration();
            }
        }
    }

    private void ApplyCalibration()
    {
        var c = _engine.Chain;
        double speechAvg = _calSpeechCount > 0 ? _calSpeechSum / _calSpeechCount : -18;
        double curGain = c.InputGain.GainDb;
        double newGain = Math.Clamp(curGain + (-18 - speechAvg), -24, 24);
        double gainDelta = newGain - curGain;

        c.InputGain.GainDb = newGain;
        c.Gate.Enabled = true;
        c.Gate.UseVad = false;
        c.Gate.ThresholdDb = Math.Clamp(_calNoiseFloor + gainDelta + 6, -80, 0);
        c.Compressor.Enabled = true;
        c.Compressor.ThresholdDb = -16;
        c.Compressor.MakeupDb = 3;
        BuildStages();
    }
}
