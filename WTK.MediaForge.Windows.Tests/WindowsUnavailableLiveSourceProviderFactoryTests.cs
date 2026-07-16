using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using Xunit;

namespace WTK.MediaForge.Windows.Tests;

public sealed class WindowsUnavailableLiveSourceProviderFactoryTests
{
    [Fact]
    public void Window_capture_provider_reports_planned_gpu_lease_path()
    {
        var diagnostics = new InMemoryDiagnosticsSink();
        var factory = new WindowsUnavailableLiveSourceProviderFactory(diagnostics);
        var source = new MediaForgeSourceDefinition
        {
            Id = SourceId.New(),
            Name = "Window",
            TypeId = MediaSourceTypes.WindowCapture,
            Settings = MediaSourceSettingsSerializer.ToJson(new WindowCaptureSourceSettings
            {
                WindowHandle = 123
            })
        };

        Assert.True(factory.CanCreate(MediaSourceTypes.WindowCapture));

        var ex = Assert.Throws<MediaForgeUnsupportedFeatureException>(() =>
            factory.CreateProvider(source));

        Assert.Equal($"source.{MediaSourceTypes.WindowCapture.Value}", ex.FeatureCode);
        Assert.Contains("Windows Graphics Capture", ex.Message, StringComparison.Ordinal);
        Assert.Contains(diagnostics.Diagnostics, diagnostic =>
            diagnostic.Code == "source.provider_unavailable" &&
            diagnostic.SourceId == source.Id.Value);
    }

    [Fact]
    public void Unavailable_live_factory_no_longer_claims_webcam()
    {
        var factory = new WindowsUnavailableLiveSourceProviderFactory();

        Assert.False(factory.CanCreate(MediaSourceTypes.Webcam));
    }
}
