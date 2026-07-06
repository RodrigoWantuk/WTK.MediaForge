using System.Collections.Immutable;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Graphics.Vulkan.Effects.Graph;
using WTK.MediaForge.Graphics.Vulkan.Rendering;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests.Effects;

[Trait("Category", TestCategories.Gpu)]
public sealed class VulkanEffectGraphTests
{
    [Fact]
    public void Effect_chain_two_passes_reuses_pool_textures()
    {
        if (!VulkanCompositionTestHarness.TryCreateRenderer(out _))
            return;

        using var device = VulkanHeadlessDevice.Create();
        using var pool = new VulkanGpuResourcePool(device);
        using var executor = VulkanEffectGraphExecutorFactory.CreateDefault();

        var drawObject = new RenderSourceLayerDrawObjectSnapshot
        {
            Id = Core.Identifiers.DrawObjectId.New(),
            Name = "Layer",
            Effects = ImmutableArray.Create<EffectStateSnapshot>(
                new ColorCorrectionEffectSnapshot
                {
                    Id = Core.Identifiers.EffectId.New(),
                    Enabled = true,
                    Order = 0,
                    Brightness = 0.1f
                },
                new BlurEffectSnapshot
                {
                    Id = Core.Identifiers.EffectId.New(),
                    Enabled = true,
                    Order = 1,
                    Radius = 2f
                })
        };

        var effectContext = new VulkanEffectExecutionContext
        {
            Device = device,
            Pool = pool,
            Input = new EffectPassDescriptor { Size = new FrameSize(64, 64) },
            Output = new EffectPassDescriptor { Size = new FrameSize(64, 64) }
        };

        executor.ExecuteChain(effectContext, drawObject);

        Assert.NotNull(effectContext.Output.OutputTextureId);
        Assert.Equal(1, pool.FactoryCreateCount);
    }

    [Fact]
    public void Effect_output_becomes_input_without_cpu_roundtrip()
    {
        if (!VulkanCompositionTestHarness.TryCreateRenderer(out _))
            return;

        using var device = VulkanHeadlessDevice.Create();
        using var pool = new VulkanGpuResourcePool(device);
        using var executor = VulkanEffectGraphExecutorFactory.CreateDefault();

        var drawObject = new RenderSourceLayerDrawObjectSnapshot
        {
            Id = Core.Identifiers.DrawObjectId.New(),
            Name = "Layer",
            Effects = ImmutableArray.Create<EffectStateSnapshot>(
                new ColorCorrectionEffectSnapshot
                {
                    Id = Core.Identifiers.EffectId.New(),
                    Enabled = true,
                    Order = 0
                },
                new BlurEffectSnapshot
                {
                    Id = Core.Identifiers.EffectId.New(),
                    Enabled = true,
                    Order = 1,
                    Radius = 1f
                })
        };

        var effectContext = new VulkanEffectExecutionContext
        {
            Device = device,
            Pool = pool,
            Input = new EffectPassDescriptor { Size = new FrameSize(32, 32) },
            Output = new EffectPassDescriptor { Size = new FrameSize(32, 32) }
        };

        executor.ExecuteChain(effectContext, drawObject);

        Assert.Equal(effectContext.Input.InputTextureId, effectContext.Output.OutputTextureId);
    }
}
