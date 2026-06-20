using System.Collections.Concurrent;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

public sealed unsafe class MediaForgeVulkanRenderer : IRenderBackend, IDisposable
{
    private readonly RenderThreadGuard _threadGuard;
    private readonly VulkanHeadlessDevice _deviceContext;
    private readonly VulkanExternalTextureRegistry _textureRegistry;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly ConcurrentDictionary<RenderOutputId, RenderOutputBindingSnapshot> _bindings = new();
    private int _disposed;

    public MediaForgeVulkanRenderer(RenderThreadGuard threadGuard, IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));
        _diagnostics = diagnostics;
        _deviceContext = VulkanHeadlessDevice.Create();
        _textureRegistry = new VulkanExternalTextureRegistry(_deviceContext, diagnostics);
    }

    internal MediaForgeVulkanRenderer(
        RenderThreadGuard threadGuard,
        VulkanHeadlessDevice deviceContext,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));
        _deviceContext = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _diagnostics = diagnostics;
        _textureRegistry = new VulkanExternalTextureRegistry(_deviceContext, diagnostics);
    }

    internal VulkanExternalTextureRegistry TextureRegistry => _textureRegistry;

    public int SubmitCount => Volatile.Read(ref _submitCount);

    private int _submitCount;

    public static bool TryCreate(RenderThreadGuard threadGuard, out MediaForgeVulkanRenderer? renderer)
    {
        ArgumentNullException.ThrowIfNull(threadGuard);

        try
        {
            renderer = new MediaForgeVulkanRenderer(threadGuard);
            return true;
        }
        catch
        {
            renderer = null;
            return false;
        }
    }

    public void BindOutput(RenderOutputBindingSnapshot binding)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(binding);
        _bindings[binding.OutputId] = binding;
    }

    public void UnbindOutput(RenderOutputId outputId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();
        _bindings.TryRemove(outputId, out _);
    }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        if (_bindings.TryGetValue(outputId, out var existing))
        {
            _bindings[outputId] = new RenderOutputBindingSnapshot
            {
                OutputId = existing.OutputId,
                TargetKind = existing.TargetKind,
                NativeHandle = existing.NativeHandle,
                SurfaceSize = surfaceSize,
                BindingVersion = existing.BindingVersion + 1
            };
        }
    }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(snapshot);

        Interlocked.Increment(ref _submitCount);

        List<VulkanExternalTextureLease>? textureLeases = null;
        CommandBuffer commandBuffer = default;
        Fence fence = default;

        try
        {
            textureLeases = AcquireTextureLeases(snapshot);
            var imports = textureLeases.Select(lease => lease.Import).ToList();
            commandBuffer = BeginCommandBuffer();

            foreach (var import in imports)
            {
                VulkanImageLayoutTransition.Transition(
                    _deviceContext.Vk,
                    commandBuffer,
                    import.Image,
                    ImageLayout.Undefined,
                    ImageLayout.General);
            }

            if (_deviceContext.Vk.EndCommandBuffer(commandBuffer) != Result.Success)
                throw new InvalidOperationException("vkEndCommandBuffer failed.");

            fence = CreateFence();
            SubmitCommandBuffer(commandBuffer, imports, fence);

            return new VulkanRenderFrameSubmission(
                _deviceContext,
                snapshot,
                commandBuffer,
                fence,
                textureLeases,
                _diagnostics);
        }
        catch
        {
            CleanupFailedSubmit(textureLeases, commandBuffer, fence);
            throw;
        }
    }

    private void CleanupFailedSubmit(
        List<VulkanExternalTextureLease>? textureLeases,
        CommandBuffer commandBuffer,
        Fence fence)
    {
        var vk = _deviceContext.Vk;
        var device = _deviceContext.Device;

        if (fence.Handle != 0)
            vk.DestroyFence(device, fence, null);

        if (commandBuffer.Handle != 0)
        {
            var localCommandBuffer = commandBuffer;
            vk.FreeCommandBuffers(device, _deviceContext.CommandPool, 1, &localCommandBuffer);
        }

        if (textureLeases is null)
            return;

        foreach (var lease in textureLeases)
            lease.Dispose();
    }

    public void WaitIdle()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();
        _deviceContext.WaitIdle();
    }

    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        return new ValueTask(Task.Run(_deviceContext.WaitIdle, cancellationToken).WaitAsync(timeout, cancellationToken));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _textureRegistry.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _deviceContext.Dispose();
    }

    private List<VulkanExternalTextureLease> AcquireTextureLeases(RenderFrameSnapshot snapshot)
    {
        var handles = RenderFrameSnapshotGpuFrames.CollectD3D11SharedTextures(snapshot);
        var leases = new List<VulkanExternalTextureLease>(handles.Count);

        foreach (var handle in handles)
            leases.Add(_textureRegistry.Acquire(handle));

        return leases;
    }

    private CommandBuffer BeginCommandBuffer()
    {
        var allocInfo = new CommandBufferAllocateInfo
        {
            SType = StructureType.CommandBufferAllocateInfo,
            CommandPool = _deviceContext.CommandPool,
            Level = CommandBufferLevel.Primary,
            CommandBufferCount = 1
        };

        if (_deviceContext.Vk.AllocateCommandBuffers(_deviceContext.Device, &allocInfo, out CommandBuffer commandBuffer) !=
            Result.Success)
        {
            throw new InvalidOperationException("vkAllocateCommandBuffers failed.");
        }

        var beginInfo = new CommandBufferBeginInfo
        {
            SType = StructureType.CommandBufferBeginInfo,
            Flags = CommandBufferUsageFlags.OneTimeSubmitBit
        };

        if (_deviceContext.Vk.BeginCommandBuffer(commandBuffer, &beginInfo) != Result.Success)
            throw new InvalidOperationException("vkBeginCommandBuffer failed.");

        return commandBuffer;
    }

    private Fence CreateFence()
    {
        var fenceInfo = new FenceCreateInfo
        {
            SType = StructureType.FenceCreateInfo,
            Flags = FenceCreateFlags.None
        };

        if (_deviceContext.Vk.CreateFence(_deviceContext.Device, &fenceInfo, null, out Fence fence) != Result.Success)
            throw new InvalidOperationException("vkCreateFence failed.");

        return fence;
    }

    private void SubmitCommandBuffer(
        CommandBuffer commandBuffer,
        IReadOnlyList<VulkanD3D11TextureImport> imports,
        Fence fence)
    {
        var vk = _deviceContext.Vk;
        var device = _deviceContext.Device;

        void* submitPNext = null;
        Win32KeyedMutexAcquireReleaseInfoKHR keyedMutexInfo = default;

        if (imports.Count > 0)
        {
            var acquireSyncs = stackalloc DeviceMemory[imports.Count];
            var releaseSyncs = stackalloc DeviceMemory[imports.Count];
            var acquireKeys = stackalloc ulong[imports.Count];
            var releaseKeys = stackalloc ulong[imports.Count];
            var acquireTimeouts = stackalloc uint[imports.Count];

            for (var i = 0; i < imports.Count; i++)
            {
                acquireSyncs[i] = imports[i].Memory;
                releaseSyncs[i] = imports[i].Memory;
                acquireKeys[i] = D3D11SharedTextureSyncKeys.Consumer;
                releaseKeys[i] = D3D11SharedTextureSyncKeys.Producer;
                acquireTimeouts[i] = 1_000_000_000;
            }

            keyedMutexInfo = new Win32KeyedMutexAcquireReleaseInfoKHR
            {
                SType = StructureType.Win32KeyedMutexAcquireReleaseInfoKhr,
                AcquireCount = (uint)imports.Count,
                PAcquireSyncs = acquireSyncs,
                PAcquireKeys = acquireKeys,
                PAcquireTimeouts = acquireTimeouts,
                ReleaseCount = (uint)imports.Count,
                PReleaseSyncs = releaseSyncs,
                PReleaseKeys = releaseKeys
            };

            submitPNext = &keyedMutexInfo;
        }

        var commandBuffers = stackalloc CommandBuffer[] { commandBuffer };

        var submitInfo = new SubmitInfo
        {
            SType = StructureType.SubmitInfo,
            PNext = submitPNext,
            CommandBufferCount = 1,
            PCommandBuffers = commandBuffers
        };

        if (vk.QueueSubmit(_deviceContext.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
            throw new InvalidOperationException("vkQueueSubmit failed.");
    }
}
