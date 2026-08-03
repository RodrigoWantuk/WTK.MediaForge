using Avalonia.Controls;

namespace WTK.MediaForge.Studio.Services;

/// <summary>Registers the platform-specific native preview host for the Studio shell.</summary>
public static class StudioPreviewHostFactory
{
    private static Func<Control>? _factory;

    /// <summary>Registers the native control factory during platform bootstrap.</summary>
    public static void Configure(Func<Control> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    /// <summary>Creates the current platform native preview host, when available.</summary>
    public static Control? Create() => _factory?.Invoke();
}
