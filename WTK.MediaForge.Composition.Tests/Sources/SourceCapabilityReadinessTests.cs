using WTK.MediaForge.Composition.Sources;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Sources;

public sealed class SourceCapabilityReadinessTests
{
    [Fact]
    public void Static_image_is_product_validated_but_video_file_remains_prototype()
    {
        var entries = MediaSourceTypeRegistry.CreateCapabilityEntries();

        var image = Assert.Single(entries, entry => entry.Id == $"source.{MediaSourceTypes.ImageFile.Value}");
        Assert.Equal(MediaForgeSupportStatus.Supported, image.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.ProductValidated, image.ProductReadinessStatus);
        Assert.Equal(MediaTransportKind.StaticCpuAsset, image.TransportKind);

        var video = Assert.Single(entries, entry => entry.Id == $"source.{MediaSourceTypes.VideoFile.Value}");
        Assert.Equal(MediaForgeSupportStatus.PrototypeOnly, video.SupportStatus);
        Assert.Equal(MediaForgeProductReadinessStatus.Prototype, video.ProductReadinessStatus);
        Assert.False(string.IsNullOrWhiteSpace(video.UnavailableReason));
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
}
