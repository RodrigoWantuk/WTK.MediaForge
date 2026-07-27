using WTK.MediaForge.Composition.Runtime.Rendering;
using Xunit;

namespace WTK.MediaForge.Composition.Tests.Rendering;

public sealed class PhysicalRenderGraphExecutionContextTests
{
    [Fact]
    public void Fan_out_resource_is_released_only_after_its_last_physical_consumer_completes()
    {
        var context = new PhysicalRenderGraphExecutionContext<TestResource>(CreateFanOutPlan());
        var source = new TestResource("source");

        context.Publish("source:camera", source);
        Assert.Empty(context.CompleteOperation("source:camera"));

        Assert.Same(source, context.GetRequiredDependency("output:preview", "source:camera"));
        Assert.Empty(context.CompleteOperation("output:preview"));
        Assert.False(context.HasReturnedToBaseline);

        Assert.Same(source, context.GetRequiredDependency("output:program", "source:camera"));
        Assert.Equal([source], context.CompleteOperation("output:program"));
        Assert.True(context.HasReturnedToBaseline);
        Assert.Equal(1, context.Metrics.HighWaterMark);
    }

    [Fact]
    public void Encoded_dispatch_retires_output_resource_after_dispatch_completion()
    {
        var context = new PhysicalRenderGraphExecutionContext<TestResource>(CreateEncodedOutputPlan());
        var output = new TestResource("encoded-output");

        context.Publish("output:recording", output);
        Assert.Empty(context.CompleteOperation("output:recording"));

        Assert.Same(output, context.GetRequiredDependency("encode-dispatch:recording", "output:recording"));
        Assert.Equal([output], context.CompleteOperation("encode-dispatch:recording"));
        Assert.True(context.HasReturnedToBaseline);
    }

    [Fact]
    public void Abort_returns_every_published_resource_once_in_reverse_physical_order()
    {
        var context = new PhysicalRenderGraphExecutionContext<TestResource>(CreateEncodedOutputPlan());
        var output = new TestResource("output");
        var dispatch = new TestResource("dispatch");

        context.Publish("output:recording", output);
        context.Publish("encode-dispatch:recording", dispatch);

        Assert.Equal([dispatch, output], context.Abort());
        Assert.Empty(context.Abort());
        Assert.True(context.HasReturnedToBaseline);
        Assert.Equal(2, context.Metrics.AbortedResources);
    }

    [Fact]
    public void Context_rejects_duplicate_completion_and_undeclared_dependencies()
    {
        var context = new PhysicalRenderGraphExecutionContext<TestResource>(CreateEncodedOutputPlan());
        var output = new TestResource("output");

        context.Publish("output:recording", output);
        Assert.Empty(context.CompleteOperation("output:recording"));

        Assert.Throws<InvalidOperationException>(() =>
            context.GetRequiredDependency("encode-dispatch:recording", "missing"));

        Assert.Equal([output], context.CompleteOperation("encode-dispatch:recording"));
        Assert.Throws<InvalidOperationException>(() => context.CompleteOperation("encode-dispatch:recording"));
    }

    private static PhysicalRenderGraphPlan CreateFanOutPlan() =>
        new(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.AcquireSourceFrame,
                Key = "source:camera",
                Consumers = ["output:preview", "output:program"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:preview",
                Dependencies = ["source:camera"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:program",
                Dependencies = ["source:camera"]
            }
        ]);

    private static PhysicalRenderGraphPlan CreateEncodedOutputPlan() =>
        new(
        [
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.RenderOutput,
                Key = "output:recording",
                Consumers = ["encode-dispatch:recording"]
            },
            new PhysicalRenderGraphOperation
            {
                Kind = PhysicalRenderGraphOperationKind.DispatchEncodedOutput,
                Key = "encode-dispatch:recording",
                Dependencies = ["output:recording"]
            }
        ]);

    private sealed record TestResource(string Name);
}
