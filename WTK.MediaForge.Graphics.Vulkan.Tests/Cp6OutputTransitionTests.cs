using WTK.MediaForge.Composition.Outputs;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;
using Xunit;

namespace WTK.MediaForge.Graphics.Vulkan.Tests;

[Trait("Category", TestCategories.Gpu)]
[Collection("VulkanComposition")]
public sealed class Cp6OutputTransitionTests
{
    [Theory]
    [InlineData(0f, 255, 0, 0)]
    [InlineData(0.5f, 128, 0, 128)]
    [InlineData(1f, 0, 0, 255)]
    public async Task Output_fade_transition_crossfades_between_previous_and_current_scene(
        float progress,
        byte expectedR,
        byte expectedG,
        byte expectedB)
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var previousCanvasId = CanvasId.New();
                var currentCanvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = new RenderFrameSnapshot
                {
                    ProjectStateVersion = 1,
                    Canvases =
                    [
                        VulkanCompositionTestHarness.CreateSolidCanvas(previousCanvasId, size, ColorRgba.From(1, 0, 0, 1)),
                        VulkanCompositionTestHarness.CreateSolidCanvas(currentCanvasId, size, ColorRgba.From(0, 0, 1, 1))
                    ],
                    Outputs =
                    [
                        new RenderOutputStateSnapshot
                        {
                            Id = outputId,
                            Name = "Program",
                            TypeId = RenderOutputTypes.Offscreen,
                            CanvasId = currentCanvasId,
                            PreviousCanvasId = previousCanvasId,
                            RouteTransitionKind = OutputRouteTransitionKind.Fade,
                            RouteTransitionProgress = progress,
                            OutputSize = size,
                            CanvasLayoutMode = LayoutMode.Stretch,
                            LetterboxColor = ColorRgba.Black
                        }
                    ]
                };

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR, expectedG, expectedB, expectedA: 255, tolerance: 3);
            }
            finally
            {
                guard.Clear();
            }
        }
    }

    [Fact]
    public async Task Output_composition_uses_physical_plan_dependency_instead_of_snapshot_transition_fallback()
    {
        if (!VulkanCompositionTestHarness.TryCreateCompositionContext(out var context))
            return;

        using (context)
        {
            var guard = context!.Guard;
            var backend = context.Backend;
            guard.BindToCurrentThread();

            try
            {
                var outputId = RenderOutputId.New();
                var previousCanvasId = CanvasId.New();
                var currentCanvasId = CanvasId.New();
                var size = new FrameSize(64, 64);

                backend.BindOutput(VulkanCompositionTestHarness.CreateOffscreenBinding(outputId, size.Width, size.Height));

                using var snapshot = new RenderFrameSnapshot
                {
                    ProjectStateVersion = 1,
                    Canvases =
                    [
                        VulkanCompositionTestHarness.CreateSolidCanvas(previousCanvasId, size, ColorRgba.From(1, 0, 0, 1)),
                        VulkanCompositionTestHarness.CreateSolidCanvas(currentCanvasId, size, ColorRgba.From(0, 0, 1, 1))
                    ],
                    Outputs =
                    [
                        new RenderOutputStateSnapshot
                        {
                            Id = outputId,
                            Name = "Program",
                            TypeId = RenderOutputTypes.Offscreen,
                            CanvasId = currentCanvasId,
                            PreviousCanvasId = previousCanvasId,
                            RouteTransitionKind = OutputRouteTransitionKind.Fade,
                            RouteTransitionProgress = 0f,
                            OutputSize = size,
                            CanvasLayoutMode = LayoutMode.Stretch,
                            LetterboxColor = ColorRgba.Black
                        }
                    ]
                };

                const string canvasKey = "canvas:current";
                const string outputKey = "output:program";
                snapshot.RenderGraphExecution = new RenderGraphExecutionResult(
                    executedNodeKeys: [canvasKey, outputKey],
                    skippedNodeKeys: [],
                    nodeResults: new Dictionary<string, RenderGraphNodeResult>(StringComparer.Ordinal),
                    physicalPlan: new PhysicalRenderGraphPlan(
                    [
                        new PhysicalRenderGraphOperation
                        {
                            Kind = PhysicalRenderGraphOperationKind.RenderCanvas,
                            Key = canvasKey,
                            Name = "Current canvas",
                            CanvasId = currentCanvasId,
                            Consumers = [outputKey]
                        },
                        new PhysicalRenderGraphOperation
                        {
                            Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                            Key = outputKey,
                            Name = "Program output",
                            Dependencies = [canvasKey],
                            OutputId = outputId,
                            CanvasId = currentCanvasId
                        }
                    ]));

                var submission = backend.Submit(snapshot);
                await VulkanCompositionTestHarness.ReleaseSubmissionAsync(submission);

                Assert.True(backend.TryReadOffscreenPixel(outputId, 32, 32, out var pixel));
                VulkanCompositionTestHarness.AssertPixelNear(pixel, expectedR: 0, expectedG: 0, expectedB: 255, expectedA: 255, tolerance: 3);
            }
            finally
            {
                guard.Clear();
            }
        }
    }
}
