using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Sources;
using WTK.MediaForge.Core.Gpu;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Sources;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Sources;

public sealed class CompositeMediaSourceProviderFactoryTests
{
    [Fact]
    public void CanCreate_returns_true_when_any_inner_factory_supports_type()
    {
        var supportedType = MediaSourceTypeId.From("wtk.test.supported");
        var factory = new CompositeMediaSourceProviderFactory(
            new TestSourceProviderFactory(MediaSourceTypeId.From("wtk.test.other")),
            new TestSourceProviderFactory(supportedType));

        Assert.True(factory.CanCreate(supportedType));
        Assert.False(factory.CanCreate(MediaSourceTypeId.From("wtk.test.missing")));
    }

    [Fact]
    public void CreateProvider_uses_first_factory_that_supports_source_type()
    {
        var supportedType = MediaSourceTypeId.From("wtk.test.supported");
        var firstProvider = new TestVideoFrameProvider(SourceId.New(), "first");
        var secondProvider = new TestVideoFrameProvider(SourceId.New(), "second");
        var first = new TestSourceProviderFactory(supportedType, firstProvider);
        var second = new TestSourceProviderFactory(supportedType, secondProvider);
        var factory = new CompositeMediaSourceProviderFactory(first, second);

        var provider = factory.CreateProvider(new MediaForgeSourceDefinition
        {
            TypeId = supportedType,
            Name = "Source"
        });

        Assert.Same(firstProvider, provider);
        Assert.Equal(1, first.CreateProviderCalls);
        Assert.Equal(0, second.CreateProviderCalls);
    }

    [Fact]
    public void CreateProvider_for_missing_type_throws_observable_unsupported_feature()
    {
        var factory = new CompositeMediaSourceProviderFactory(
            new TestSourceProviderFactory(MediaSourceTypeId.From("wtk.test.other")));

        var ex = Assert.Throws<MediaForgeUnsupportedFeatureException>(() =>
            factory.CreateProvider(new MediaForgeSourceDefinition
            {
                TypeId = MediaSourceTypeId.From("wtk.test.missing"),
                Name = "Missing"
            }));

        Assert.Equal("wtk.test.missing", ex.FeatureCode);
    }

    private sealed class TestSourceProviderFactory(
        MediaSourceTypeId supportedType,
        IVideoFrameProvider? provider = null) : IMediaSourceProviderFactory
    {
        public int CreateProviderCalls { get; private set; }

        public bool CanCreate(MediaSourceTypeId typeId) => typeId == supportedType;

        public IVideoFrameProvider CreateProvider(MediaForgeSourceDefinition sourceDefinition)
        {
            CreateProviderCalls++;
            return provider ?? new TestVideoFrameProvider(sourceDefinition.Id, sourceDefinition.Name);
        }
    }

    private sealed class TestVideoFrameProvider(SourceId id, string name) : IVideoFrameProvider
    {
        public SourceId Id { get; } = id;

        public string Name { get; } = name;

        public MediaSourceState State => MediaSourceState.Stopped;

        public Exception? LastError => null;

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public bool TryAcquireLatestFrame(out GpuFrameLease lease)
        {
            lease = null!;
            return false;
        }
    }
}
