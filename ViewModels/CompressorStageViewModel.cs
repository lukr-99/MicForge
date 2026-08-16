using System;
using MicForge.Audio;

namespace MicForge.ViewModels;

/// <summary>Compressor stage that also exposes the processor for the transfer-curve view.</summary>
public sealed class CompressorStageViewModel : StageViewModel
{
    public CompressorStageViewModel(Compressor compressor, string accent,
        Func<bool> getEnabled, Action<bool> setEnabled)
        : base("Compressor", accent, getEnabled, setEnabled)
    {
        Compressor = compressor;
    }

    public override bool IsCompressor => true;

    public Compressor Compressor { get; }
}
