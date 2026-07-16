using WTK.MediaForge.Composition;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Composition.Sources.Settings;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Windows.Media.Ndi;
using Xunit;

namespace WTK.MediaForge.Windows.Tests;

public sealed class WindowsNdiSourceProviderFactoryTests
{
    [Fact]
    public void Ndi_source_factory_claims_ndi_input_only()
    {
        var factory = new WindowsNdiSourceProviderFactory(runtimeProbe: new FakeNdiRuntimeProbe(MissingRuntime()));

        Assert.True(factory.CanCreate(MediaSourceTypes.NdiInput));
        Assert.False(factory.CanCreate(MediaSourceTypes.Webcam));
    }

    [Fact]
    public void Ndi_source_factory_rejects_runtime_without_gpu_safe_path()
    {
        var factory = new WindowsNdiSourceProviderFactory(runtimeProbe: new FakeNdiRuntimeProbe(new WindowsNdiRuntimeInfo(
            IsRuntimePresent: true,
            IsLoadable: true,
            LibraryPath: @"C:\NDI\Processing.NDI.Lib.x64.dll",
            Version: "NDI test runtime",
            Reason: "Detected.")));
        var definition = new MediaForgeSourceDefinition
        {
            Id = SourceId.New(),
            Name = "NDI Camera",
            TypeId = MediaSourceTypes.NdiInput,
            Settings = MediaSourceSettingsSerializer.ToJson(new NdiInputSourceSettings { SourceName = "Camera" })
        };

        var ex = Assert.Throws<MediaForgeUnsupportedFeatureException>(() =>
            factory.CreateProvider(definition));

        Assert.Contains("not enabled", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Continuous raw CPU NDI frames are prohibited", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static WindowsNdiRuntimeInfo MissingRuntime() =>
        new(
            IsRuntimePresent: false,
            IsLoadable: false,
            LibraryPath: null,
            Version: null,
            Reason: "Missing.");

    private sealed class FakeNdiRuntimeProbe(WindowsNdiRuntimeInfo info) : IWindowsNdiRuntimeProbe
    {
        public WindowsNdiRuntimeInfo Probe() => info;
    }
}
