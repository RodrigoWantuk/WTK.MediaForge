using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Sources;

public sealed class SourceCapabilityReadinessTests
{
    [Fact]
    public void Static_image_is_product_validated_but_video_file_requires_composite_product_proofs()
    {
        var entries = MediaSourceTypeRegistry.CreateCapabilityEntries();

        var image = Assert.Single(entries, entry => entry.Id == $"source.{MediaSourceTypes.ImageFile.Value}");
        Assert.Equal(MediaForgeSupportStatus.Supported, image.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, image.ProductReadinessStatus);
        Assert.Equal(MediaTransportKind.StaticCpuAsset, image.TransportKind);

        var video = Assert.Single(entries, entry => entry.Id == $"source.{MediaSourceTypes.VideoFile.Value}");
        Assert.Equal(MediaForgeSupportStatus.Unavailable, video.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.Contract, video.ProductReadinessStatus);
        Assert.False(string.IsNullOrWhiteSpace(video.UnavailableReason));
        Assert.Contains("hardware decode", video.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Window_capture_is_not_available_until_gpu_provider_exists()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(
            new HardwareMediaCapabilityReport { Platform = "Test" },
            MediaSourceTypeRegistry.CreateCapabilityEntries());

        var window = Assert.Single(
            report.Entries,
            entry => entry.Id == $"source.{MediaSourceTypes.WindowCapture.Value}");

        Assert.Equal(MediaForgeSupportStatus.Planned, window.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.Contract, window.ProductReadinessStatus);
        Assert.False(report.IsFeatureAvailable(window.Id));
        Assert.Contains("Windows Graphics Capture", window.UnavailableReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Webcam_is_not_available_until_gpu_upload_provider_exists()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(
            new HardwareMediaCapabilityReport { Platform = "Test" },
            MediaSourceTypeRegistry.CreateCapabilityEntries());

        var webcam = Assert.Single(
            report.Entries,
            entry => entry.Id == $"source.{MediaSourceTypes.Webcam.Value}");

        Assert.Equal(MediaForgeSupportStatus.Planned, webcam.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.Contract, webcam.ProductReadinessStatus);
        Assert.Equal(MediaTransportKind.GpuSurface, webcam.TransportKind);
        Assert.False(report.IsFeatureAvailable(webcam.Id));
        Assert.Contains("webcam provider", webcam.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ndi_input_is_unsupported_until_license_and_gpu_path_are_validated()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(
            new HardwareMediaCapabilityReport { Platform = "Test" },
            MediaSourceTypeRegistry.CreateCapabilityEntries());

        var ndi = Assert.Single(
            report.Entries,
            entry => entry.Id == $"source.{MediaSourceTypes.NdiInput.Value}");

        Assert.Equal(MediaForgeSupportStatus.Unsupported, ndi.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.Contract, ndi.ProductReadinessStatus);
        Assert.False(report.IsFeatureAvailable(ndi.Id));
        Assert.Contains("license", ndi.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Source_capability_entries_do_not_mark_skeleton_or_prototype_as_available()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(
            new HardwareMediaCapabilityReport { Platform = "Test" },
            MediaSourceTypeRegistry.CreateCapabilityEntries());

        foreach (var entry in report.Entries.Where(entry => entry.Category == CapabilityCategories.Source))
        {
            if (entry.ProductReadinessStatus is
                MediaForgeProductReadinessStatus.Prototype or
                MediaForgeProductReadinessStatus.Skeleton)
            {
                Assert.False(report.IsFeatureAvailable(entry.Id));
            }
        }
    }

    [Fact]
    public void Unavailable_source_capabilities_have_user_visible_reasons()
    {
        var report = MediaForgeCapabilityReportBuilder.Build(
            new HardwareMediaCapabilityReport { Platform = "Test" },
            MediaSourceTypeRegistry.CreateCapabilityEntries());

        foreach (var entry in report.Entries.Where(entry => entry.Category == CapabilityCategories.Source))
        {
            if (report.IsFeatureAvailable(entry.Id))
                continue;

            Assert.False(string.IsNullOrWhiteSpace(entry.UnavailableReason));
        }
    }
}
