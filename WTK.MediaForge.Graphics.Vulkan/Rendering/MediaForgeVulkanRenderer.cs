using System.Buffers;
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

internal sealed unsafe class MediaForgeVulkanRenderer : IRenderBackend, IDisposable
{
    private const int MaxExternalTextureImportsPerSubmit = 128;

    private readonly RenderThreadGuard _threadGuard;
    private readonly VulkanHeadlessDevice _deviceContext;
    private readonly VulkanExternalTextureRegistry _textureRegistry;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly IVulkanRendererFaultInjector _faultInjector;
    private readonly ConcurrentDictionary<RenderOutputId, RenderOutputBindingSnapshot> _bindings = new();
    private readonly ConcurrentDictionary<RenderOutputId, VulkanOffscreenRenderTarget> _offscreenTargets = new();
    private int _disposed;

    internal MediaForgeVulkanRenderer(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        IVulkanRendererFaultInjector faultInjector)
        : this(threadGuard, VulkanHeadlessDevice.Create(), diagnostics, faultInjector)
    {
    }

    internal MediaForgeVulkanRenderer(
        RenderThreadGuard threadGuard,
        VulkanHeadlessDevice deviceContext,
        IMediaForgeDiagnosticsSink? diagnostics,
        IVulkanRendererFaultInjector faultInjector)
    {
        _threadGuard = threadGuard ?? throw new ArgumentNullException(nameof(threadGuard));
        _deviceContext = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _diagnostics = diagnostics;
        _faultInjector = faultInjector ?? throw new ArgumentNullException(nameof(faultInjector));
        _textureRegistry = new VulkanExternalTextureRegistry(_deviceContext, diagnostics);
    }

    internal VulkanExternalTextureRegistry TextureRegistry => _textureRegistry;

    internal int TextureRegistryActiveLeaseCount => _textureRegistry.ActiveLeaseCount;

    internal int OffscreenTargetCount => _offscreenTargets.Count;

    internal bool TryGetOffscreenTargetSize(RenderOutputId outputId, out FrameSize size)
    {
        if (_offscreenTargets.TryGetValue(outputId, out var target))
        {
            size = target.Size;
            return true;
        }

        size = default;
        return false;
    }

    internal IVulkanRendererFaultInjector FaultInjector => _faultInjector;

    public int SubmitCount => Volatile.Read(ref _submitCount);

    private int _submitCount;

    internal static bool TryCreate(
        RenderThreadGuard threadGuard,
        IMediaForgeDiagnosticsSink? diagnostics,
        IVulkanRendererFaultInjector faultInjector,
        out MediaForgeVulkanRenderer? renderer)
    {
        ArgumentNullException.ThrowIfNull(threadGuard);
        ArgumentNullException.ThrowIfNull(faultInjector);

        try
        {
            renderer = new MediaForgeVulkanRenderer(threadGuard, diagnostics, faultInjector);
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

        if (binding.TargetKind == RenderTargetKind.Offscreen)
        {
            if (binding.SurfaceSize.Width == 0 || binding.SurfaceSize.Height == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(binding),
                    "Offscreen output binding requires non-zero surface dimensions.");
            }

            if (_offscreenTargets.TryRemove(binding.OutputId, out var existing))
                existing.Dispose();

            _offscreenTargets[binding.OutputId] = new VulkanOffscreenRenderTarget(
                _deviceContext,
                binding.SurfaceSize);
        }

        _bindings[binding.OutputId] = binding;
    }

    public void UnbindOutput(RenderOutputId outputId)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        _bindings.TryRemove(outputId, out _);

        if (_offscreenTargets.TryRemove(outputId, out var target))
            target.Dispose();
    }

    public void ResizeOutput(RenderOutputId outputId, FrameSize surfaceSize)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        if (surfaceSize.Width == 0 || surfaceSize.Height == 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(surfaceSize),
                "Output surface dimensions must be greater than zero.");
        }

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

            if (existing.TargetKind == RenderTargetKind.Offscreen &&
                _offscreenTargets.TryGetValue(outputId, out var target))
            {
                target.Resize(surfaceSize);
            }
        }
    }

    public IRenderFrameSubmission Submit(RenderFrameSnapshot snapshot)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();
        ArgumentNullException.ThrowIfNull(snapshot);

        Interlocked.Increment(ref _submitCount);

        var handles = RenderFrameSnapshotGpuFrames.CollectD3D11SharedTextures(snapshot);

        if (handles.Count > MaxExternalTextureImportsPerSubmit)
        {
            throw new NotSupportedException(
                $"Submit supports at most {MaxExternalTextureImportsPerSubmit} external textures.");
        }

        List<VulkanExternalTextureLease>? textureLeases = null;
        CommandBuffer commandBuffer = default;
        Fence fence = default;

        try
        {
            textureLeases = AcquireTextureLeases(handles);
            var imports = textureLeases.Select(lease => lease.Import).ToList();
            var previousLayouts = imports.Select(import => import.CurrentLayout).ToArray();
            commandBuffer = BeginCommandBuffer();

            try
            {
                foreach (var import in imports)
                {
                    VulkanImageLayoutTransition.Transition(
                        _deviceContext.Vk,
                        commandBuffer,
                        import.Image,
                        import.CurrentLayout,
                        ImageLayout.General);
                }

                if (_deviceContext.Vk.EndCommandBuffer(commandBuffer) != Result.Success)
                    throw new InvalidOperationException("vkEndCommandBuffer failed.");

                fence = CreateFence();
                SubmitCommandBuffer(commandBuffer, imports, fence);

                foreach (var import in imports)
                    import.SetLayout(ImageLayout.General);
            }
            catch
            {
                for (var i = 0; i < imports.Count; i++)
                    imports[i].SetLayout(previousLayouts[i]);

                throw;
            }

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
        {
            try
            {
                lease.Dispose();
            }
            catch (Exception ex)
            {
                MediaForgeDiagnostics.Report(
                    _diagnostics,
                    MediaForgeDiagnosticSeverity.Error,
                    "vulkan.texture_lease_dispose_failed",
                    "Failed to dispose texture lease after failed submit.",
                    nameof(MediaForgeVulkanRenderer),
                    ex);
            }
        }
    }

    /// <summary>
    /// No-op: submission ownership belongs to <see cref="PendingRenderSubmissionTracker"/>.
    /// Do not create untracked internal submissions in this backend.
    /// Shutdown waits each submission fence via the tracker.
    /// </summary>
    public ValueTask WaitIdleAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        _threadGuard.AssertOnRenderThread();

        _ = timeout;
        _ = cancellationToken;

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var target in _offscreenTargets.Values)
            target.Dispose();

        _offscreenTargets.Clear();

        _textureRegistry.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _deviceContext.Dispose();
    }

    private List<VulkanExternalTextureLease> AcquireTextureLeases(IReadOnlyList<D3D11SharedTextureFrameHandle> handles)
    {
        var leases = new List<VulkanExternalTextureLease>(handles.Count);
        var acquireAttempt = 0;

        try
        {
            foreach (var handle in handles)
            {
                acquireAttempt++;
                _faultInjector.BeforeAcquireTexture(acquireAttempt);
                leases.Add(_textureRegistry.Acquire(handle));
            }

            return leases;
        }
        catch
        {
            foreach (var lease in leases)
            {
                try
                {
                    lease.Dispose();
                }
                catch (Exception ex)
                {
                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "vulkan.texture_lease_dispose_failed",
                        "Failed to dispose texture lease after partial acquire failure.",
                        nameof(MediaForgeVulkanRenderer),
                        ex);
                }
            }

            throw;
        }
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

        DeviceMemory[]? acquireSyncs = null;
        DeviceMemory[]? releaseSyncs = null;
        ulong[]? acquireKeys = null;
        ulong[]? releaseKeys = null;
        uint[]? acquireTimeouts = null;

        try
        {
            void* submitPNext = null;
            Win32KeyedMutexAcquireReleaseInfoKHR keyedMutexInfo = default;

            if (imports.Count > 0)
            {
                acquireSyncs = ArrayPool<DeviceMemory>.Shared.Rent(imports.Count);
                releaseSyncs = ArrayPool<DeviceMemory>.Shared.Rent(imports.Count);
                acquireKeys = ArrayPool<ulong>.Shared.Rent(imports.Count);
                releaseKeys = ArrayPool<ulong>.Shared.Rent(imports.Count);
                acquireTimeouts = ArrayPool<uint>.Shared.Rent(imports.Count);

                for (var i = 0; i < imports.Count; i++)
                {
                    acquireSyncs[i] = imports[i].Memory;
                    releaseSyncs[i] = imports[i].Memory;
                    acquireKeys[i] = imports[i].SourceHandle.ProducerAcquireKey;
                    releaseKeys[i] = D3D11SharedTextureSyncKeys.Producer;
                    acquireTimeouts[i] = 1_000_000_000;
                }

                fixed (DeviceMemory* acquireSyncPtr = acquireSyncs)
                fixed (DeviceMemory* releaseSyncPtr = releaseSyncs)
                fixed (ulong* acquireKeysPtr = acquireKeys)
                fixed (ulong* releaseKeysPtr = releaseKeys)
                fixed (uint* acquireTimeoutsPtr = acquireTimeouts)
                {
                    keyedMutexInfo = new Win32KeyedMutexAcquireReleaseInfoKHR
                    {
                        SType = StructureType.Win32KeyedMutexAcquireReleaseInfoKhr,
                        AcquireCount = (uint)imports.Count,
                        PAcquireSyncs = acquireSyncPtr,
                        PAcquireKeys = acquireKeysPtr,
                        PAcquireTimeouts = acquireTimeoutsPtr,
                        ReleaseCount = (uint)imports.Count,
                        PReleaseSyncs = releaseSyncPtr,
                        PReleaseKeys = releaseKeysPtr
                    };

                    submitPNext = &keyedMutexInfo;

                    var commandBuffers = stackalloc CommandBuffer[1];
                    commandBuffers[0] = commandBuffer;

                    var submitInfo = new SubmitInfo
                    {
                        SType = StructureType.SubmitInfo,
                        PNext = submitPNext,
                        CommandBufferCount = 1,
                        PCommandBuffers = commandBuffers
                    };

                    _faultInjector.BeforeQueueSubmit();

                    if (vk.QueueSubmit(_deviceContext.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
                        throw new InvalidOperationException("vkQueueSubmit failed.");
                }
            }
            else
            {
                var commandBuffers = stackalloc CommandBuffer[1];
                commandBuffers[0] = commandBuffer;

                var submitInfo = new SubmitInfo
                {
                    SType = StructureType.SubmitInfo,
                    PNext = submitPNext,
                    CommandBufferCount = 1,
                    PCommandBuffers = commandBuffers
                };

                _faultInjector.BeforeQueueSubmit();

                if (vk.QueueSubmit(_deviceContext.GraphicsQueue, 1, &submitInfo, fence) != Result.Success)
                    throw new InvalidOperationException("vkQueueSubmit failed.");
            }

            foreach (var import in imports)
                import.SourceHandle.NotifyVulkanReleasedToProducer();
        }
        finally
        {
            if (acquireSyncs is not null)
                ArrayPool<DeviceMemory>.Shared.Return(acquireSyncs, clearArray: true);

            if (releaseSyncs is not null)
                ArrayPool<DeviceMemory>.Shared.Return(releaseSyncs, clearArray: true);

            if (acquireKeys is not null)
                ArrayPool<ulong>.Shared.Return(acquireKeys, clearArray: true);

            if (releaseKeys is not null)
                ArrayPool<ulong>.Shared.Return(releaseKeys, clearArray: true);

            if (acquireTimeouts is not null)
                ArrayPool<uint>.Shared.Return(acquireTimeouts, clearArray: true);
        }
    }
}
