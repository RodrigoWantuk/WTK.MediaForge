using WTK.MediaForge.Diagnostics;

namespace WTK.MediaForge.Windows;

public sealed class MediaForgeEngineOptions
{
    public IMediaForgeDiagnosticsSink? Diagnostics { get; init; }

    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
