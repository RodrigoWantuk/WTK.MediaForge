using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Graphics.Vulkan.Effects.Graph;

internal sealed class VulkanEffectGraphExecutor : IDisposable
{
    private readonly List<EffectNode> _nodes = [];
    private bool _disposed;

    public IReadOnlyList<EffectNode> Nodes => _nodes;

    public void Register(EffectNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        _nodes.Add(node);
    }

    public void ExecuteChain(
        VulkanEffectExecutionContext context,
        RenderDrawObjectSnapshot drawObject)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(drawObject);

        context.DrawObject = drawObject;

        foreach (var node in _nodes)
        {
            if (!node.CanApply(drawObject))
                continue;

            if (context.Output.OutputTextureId is null && context.Input.InputTextureId is null)
            {
                var acquired = context.Pool.AcquireOffscreenTarget(context.Input.Size);
                context.Input.InputTextureId = acquired.Lease.TextureId;
                context.Output.OutputTextureId = acquired.Lease.TextureId;
            }

            node.Execute(context);
            context.Input.InputTextureId = context.Output.OutputTextureId;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _nodes.Clear();
    }
}

internal static class VulkanEffectGraphExecutorFactory
{
    public static VulkanEffectGraphExecutor CreateDefault()
    {
        var executor = new VulkanEffectGraphExecutor();
        executor.Register(new ColorCorrectionEffectNode { Key = "effect.color" });
        executor.Register(new SeparableBlurEffectNode { Key = "effect.blur" });
        return executor;
    }
}
