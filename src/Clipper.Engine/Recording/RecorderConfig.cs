namespace Clipper.Engine;

/// <summary>Describes one audio track to record.</summary>
public sealed class AudioTrackConfig
{
    public required string Name { get; init; }
    public required CaptureKind Kind { get; init; }
    /// <summary>Process name (no ".exe") for ProcessInclude/ProcessExclude.</summary>
    public string? ProcessName { get; init; }
}

public sealed class RecorderConfig
{
    public int Fps { get; init; } = 60;
    public required string OutputDirectory { get; init; }
    public List<AudioTrackConfig> Tracks { get; init; } = DefaultTracks();
    public string FfmpegPath { get; init; } = "ffmpeg";
    public string NvencPreset { get; init; } = "p5";
    /// <summary>NVENC constant-quality level (lower = better/bigger). ~19-23 is a good range.</summary>
    public int Cq { get; init; } = 21;

    /// <summary>Default track layout: desktop-minus-voice, voice-only, mic.</summary>
    public static List<AudioTrackConfig> DefaultTracks(string voiceApp = "Discord") => new()
    {
        new() { Name = "Desktop", Kind = CaptureKind.ProcessExclude, ProcessName = voiceApp },
        new() { Name = "Voice",   Kind = CaptureKind.ProcessInclude, ProcessName = voiceApp },
        new() { Name = "Mic",     Kind = CaptureKind.Mic },
    };
}
