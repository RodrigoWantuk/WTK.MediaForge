using WTK.MediaForge.Diagnostics;
using WTK.MediaForge.Graphics.D3D11;

namespace WTK.MediaForge.Graphics.Vulkan.Rendering;

public sealed class VulkanExternalTextureRegistry : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly VulkanHeadlessDevice _deviceContext;
    private readonly IMediaForgeDiagnosticsSink? _diagnostics;
    private readonly Dictionary<VulkanExternalTextureKey, RegistryEntry> _entries = new();
    private bool _disposed;

    internal VulkanExternalTextureRegistry(
        VulkanHeadlessDevice deviceContext,
        IMediaForgeDiagnosticsSink? diagnostics = null)
    {
        _deviceContext = deviceContext ?? throw new ArgumentNullException(nameof(deviceContext));
        _diagnostics = diagnostics;
    }

    internal int EntryCount
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    internal int ActiveLeaseCount
    {
        get
        {
            lock (_gate)
                return _entries.Values.Sum(static entry => entry.RefCount);
        }
    }

    public VulkanExternalTextureLease Acquire(D3D11SharedTextureFrameHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        if (!handle.HasSharedHandle)
        {
            throw new ObjectDisposedException(
                nameof(handle),
                "D3D11 shared texture handle is closed or unavailable.");
        }

        var key = VulkanExternalTextureKey.From(handle);

        while (true)
        {
            RegistryEntry? entry = null;
            var isCreator = false;

            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                if (!handle.HasSharedHandle)
                {
                    throw new ObjectDisposedException(
                        nameof(handle),
                        "D3D11 shared texture handle is closed or unavailable.");
                }

                if (_entries.TryGetValue(key, out entry))
                {
                    if (entry.IsReady)
                    {
                        entry.RefCount++;
                        return new VulkanExternalTextureLease(entry, this);
                    }

                    if (entry.Creation.Task.IsFaulted)
                    {
                        _entries.Remove(key);
                        entry = null;
                    }
                }

                if (entry is null)
                {
                    entry = new RegistryEntry(key, handle);
                    _entries[key] = entry;
                    isCreator = true;
                }
            }

            if (isCreator)
            {
                try
                {
                    var import = VulkanD3D11TextureImport.Import(_deviceContext, handle);

                    lock (_gate)
                    {
                        ObjectDisposedException.ThrowIf(_disposed, this);

                        if (!_entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry))
                        {
                            import.Dispose();
                            throw new ObjectDisposedException(
                                nameof(VulkanExternalTextureRegistry),
                                "Registry entry was removed while creating import.");
                        }

                        current.PublishImport(import, handle);
                        current.RefCount++;
                        return new VulkanExternalTextureLease(current, this);
                    }
                }
                catch (Exception ex)
                {
                    lock (_gate)
                    {
                        if (_entries.TryGetValue(key, out var current) && ReferenceEquals(current, entry))
                        {
                            current.Creation.TrySetException(ex);
                            _entries.Remove(key);
                        }
                    }

                    MediaForgeDiagnostics.Report(
                        _diagnostics,
                        MediaForgeDiagnosticSeverity.Error,
                        "vulkan.texture_import_failed",
                        ex.Message,
                        nameof(VulkanExternalTextureRegistry),
                        ex);

                    throw;
                }
            }

            try
            {
                entry!.Creation.Task.GetAwaiter().GetResult();
            }
            catch
            {
            }
        }
    }

    internal void Release(VulkanExternalTextureKey key)
    {
        VulkanD3D11TextureImport? importToDispose = null;

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return;

            entry.RefCount--;

            if (entry.RefCount < 0)
                throw new InvalidOperationException("External texture refcount underflow.");

            if (entry.RefCount == 0 && entry.SourceHandle.IsRetired)
            {
                _entries.Remove(key);
                importToDispose = entry.Import;
            }
        }

        importToDispose?.Dispose();
    }

    public void CollectUnused()
    {
        List<VulkanD3D11TextureImport>? importsToDispose = null;

        lock (_gate)
        {
            List<VulkanExternalTextureKey>? toRemove = null;

            foreach (var (key, entry) in _entries)
            {
                if (entry.SourceHandle.IsRetired && entry.RefCount == 0 && entry.Import is not null)
                {
                    (importsToDispose ??= []).Add(entry.Import);
                    (toRemove ??= []).Add(key);
                }
            }

            if (toRemove is null)
                return;

            foreach (var key in toRemove)
                _entries.Remove(key);
        }

        if (importsToDispose is null)
            return;

        foreach (var import in importsToDispose)
            import.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        RegistryEntry[] entriesSnapshot;

        lock (_gate)
        {
            if (_disposed)
                return ValueTask.CompletedTask;

            if (_entries.Values.Any(static entry => entry.RefCount > 0))
            {
                throw new InvalidOperationException(
                    "Cannot dispose VulkanExternalTextureRegistry while texture leases are active.");
            }

            _disposed = true;
            entriesSnapshot = _entries.Values.ToArray();
            _entries.Clear();
        }

        foreach (var entry in entriesSnapshot)
        {
            if (!entry.Creation.Task.IsCompleted)
                entry.Creation.TrySetCanceled();

            entry.Import?.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    internal sealed class RegistryEntry
    {
        public RegistryEntry(VulkanExternalTextureKey key, D3D11SharedTextureFrameHandle sourceHandle)
        {
            Key = key;
            SourceHandle = sourceHandle;
        }

        public VulkanExternalTextureKey Key { get; }

        public D3D11SharedTextureFrameHandle SourceHandle { get; private set; }

        public TaskCompletionSource<VulkanD3D11TextureImport> Creation { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public VulkanD3D11TextureImport? Import { get; private set; }

        public int RefCount { get; set; }

        public bool IsReady => Import is not null;

        public void PublishImport(VulkanD3D11TextureImport import, D3D11SharedTextureFrameHandle sourceHandle)
        {
            Import = import;
            SourceHandle = sourceHandle;
            Creation.TrySetResult(import);
        }
    }
}
