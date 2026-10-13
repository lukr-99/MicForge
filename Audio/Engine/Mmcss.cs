using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace MicForge.Audio;

/// <summary>
/// Registers the calling thread with the Multimedia Class Scheduler Service as a
/// "Pro Audio" task, so Windows schedules it ahead of normal work and the DSP keeps up
/// under CPU load. NAudio owns the capture and render threads and gives us no start
/// hook, so we register lazily from inside their callbacks, once per thread.
/// </summary>
/// <remarks>
/// The threads end when a stream stops and NAudio gives us no hook on them at that
/// point, so there is nowhere to call AvRevertMmThreadCharacteristics from (it must run
/// on the same thread). The registration is dropped with the thread. Buffer latency is
/// not changed here.
/// </remarks>
internal static class Mmcss
{
    private const string TaskName = "Pro Audio";

    [ThreadStatic] private static bool _done;
    private static bool _unavailable;

    [DllImport("avrt.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    /// <summary>Boosts the current thread once. Cheap after the first call on a thread.</summary>
    public static void EnsureProAudio(string threadRole)
    {
        if (_done || _unavailable) return;
        _done = true;
        try
        {
            uint index = 0;
            if (AvSetMmThreadCharacteristics(TaskName, ref index) == IntPtr.Zero)
                Log.Warn($"MMCSS: could not register the {threadRole} thread (error {Marshal.GetLastWin32Error()}).");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            _unavailable = true;
            Log.Warn("MMCSS: avrt.dll is not available; audio threads keep normal priority.");
        }
    }
}

/// <summary>Pass-through provider that boosts whichever thread pulls it (the render thread).</summary>
internal sealed class MmcssSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;

    public MmcssSampleProvider(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        Mmcss.EnsureProAudio("render");
        return _source.Read(buffer, offset, count);
    }
}
