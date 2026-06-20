using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Time;

namespace WTK.MediaForge.Core.Gpu.Slots;

public sealed class GpuFrameSlotRing : IDisposable
{
    private readonly object _gate = new();
    private readonly SlotEntry[] _slots;
    private readonly Action<Exception, int>? _onResourceDisposeFailed;
    private int? _latestSlotIndex;
    private bool _stopped;
    private bool _finalizeRequested;
    private bool _disposed;
    private long _droppedFrames;
    private long _generationMismatches;

    public GpuFrameSlotRing(
        int slotCount = 3,
        bool reusePhysicalResources = false,
        Action<Exception, int>? onResourceDisposeFailed = null)
    {
        if (slotCount < 1)
            throw new ArgumentOutOfRangeException(nameof(slotCount));

        SlotCount = slotCount;
        ReusePhysicalResources = reusePhysicalResources;
        _onResourceDisposeFailed = onResourceDisposeFailed;
        _slots = new SlotEntry[slotCount];
        for (var i = 0; i < slotCount; i++)
            _slots[i] = new SlotEntry { SlotIndex = i };
    }

    public int SlotCount { get; }

    public bool ReusePhysicalResources { get; }

    public long DroppedFrameCount => Interlocked.Read(ref _droppedFrames);

    public long GenerationMismatchCount => Interlocked.Read(ref _generationMismatches);

    public bool IsStopped
    {
        get
        {
            lock (_gate)
                return _stopped;
        }
    }

    public bool IsFullyDisposed
    {
        get
        {
            lock (_gate)
                return _disposed;
        }
    }

    public bool TryBeginWrite(out int slotIndex)
    {
        slotIndex = -1;

        lock (_gate)
        {
            if (_disposed || _stopped)
                return false;

            for (var i = 0; i < _slots.Length; i++)
            {
                var entry = _slots[i];

                if (_latestSlotIndex == i)
                    continue;

                if (entry.State == GpuFrameSlotState.Writing)
                    continue;

                if (entry.RefCount > 0)
                    continue;

                if (entry.State != GpuFrameSlotState.Free)
                    continue;

                entry.State = GpuFrameSlotState.Writing;
                slotIndex = i;
                return true;
            }

            Interlocked.Increment(ref _droppedFrames);
            return false;
        }
    }

    public void InitializeSlot(int slotIndex, IGpuFrameHandle handle)
    {
        ArgumentNullException.ThrowIfNull(handle);

        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GpuFrameSlotRing));

            var entry = _slots[slotIndex];
            entry.Handle = handle;
            entry.State = GpuFrameSlotState.Free;
            entry.Generation = 0;
            entry.RefCount = 0;
            entry.FrameNumber = 0;
            entry.ContentToken = 0;
            entry.TimestampTicks = 0;
            entry.ResourceDisposed = false;
        }
    }

    public void CompleteWrite(int slotIndex, IGpuFrameHandle? handle = null, long frameNumber = 0, long timestampTicks = 0)
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GpuFrameSlotRing));

            if (_stopped)
                throw new InvalidOperationException("Ring is stopped.");

            var entry = _slots[slotIndex];

            if (entry.State != GpuFrameSlotState.Writing)
                throw new InvalidOperationException($"Slot {slotIndex} is not in Writing state.");

            if (handle is not null)
                entry.Handle = handle;
            else if (entry.Handle is null)
                throw new InvalidOperationException($"Slot {slotIndex} has no handle.");

            entry.Generation++;
            entry.ContentToken++;
            entry.FrameNumber = frameNumber;
            entry.TimestampTicks = timestampTicks;
            entry.State = GpuFrameSlotState.Published;
            PublishLatestLocked(slotIndex);
        }
    }

    public void CancelWrite(int slotIndex)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var entry = _slots[slotIndex];

            if (entry.State != GpuFrameSlotState.Writing)
                return;

            entry.State = GpuFrameSlotState.Free;
        }
    }

    public bool TryRetainLatest(out GpuFrameSlotLease? lease)
    {
        lease = null;

        lock (_gate)
        {
            if (_disposed || _stopped)
                return false;

            if (_latestSlotIndex is not int latestIndex)
                return false;

            var entry = _slots[latestIndex];

            if (entry.State != GpuFrameSlotState.Published)
                return false;

            entry.RefCount++;
            lease = CreateLeaseLocked(latestIndex, entry);
            return true;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped)
                return;

            _stopped = true;
            _latestSlotIndex = null;

            for (var i = 0; i < _slots.Length; i++)
                FinalizeSlotOnStopLocked(_slots[i]);
        }
    }

    public void RequestFinalize()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _finalizeRequested = true;
            _stopped = true;
            _latestSlotIndex = null;

            for (var i = 0; i < _slots.Length; i++)
                FinalizeSlotOnDisposeLocked(_slots[i]);

            TryCompleteFinalizeLocked();
        }
    }

    public void Dispose() => RequestFinalize();

    internal void Release(int slotIndex, long generation)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            var entry = _slots[slotIndex];

            if (entry.Generation != generation)
            {
                ReportGenerationMismatch(slotIndex, generation, entry.Generation);
                return;
            }

            if (entry.RefCount <= 0)
            {
#if DEBUG
                throw new InvalidOperationException($"Slot {slotIndex} retain count underflow.");
#else
                return;
#endif
            }

            entry.RefCount--;

            if (entry.RefCount == 0)
                OnSlotRetainCountZeroLocked(slotIndex, entry);

            TryCompleteFinalizeLocked();
        }
    }

    internal int GetRefCount(int slotIndex)
    {
        lock (_gate)
            return _slots[slotIndex].RefCount;
    }

    internal int GetTotalRetainCount()
    {
        lock (_gate)
        {
            var total = 0;

            for (var i = 0; i < _slots.Length; i++)
                total += _slots[i].RefCount;

            return total;
        }
    }

    internal GpuFrameSlotState GetSlotState(int slotIndex)
    {
        lock (_gate)
            return _slots[slotIndex].State;
    }

    internal long GetSlotGeneration(int slotIndex)
    {
        lock (_gate)
            return _slots[slotIndex].Generation;
    }

    internal long GetSlotContentToken(int slotIndex)
    {
        lock (_gate)
            return _slots[slotIndex].ContentToken;
    }

    internal int? GetLatestSlotIndex()
    {
        lock (_gate)
            return _latestSlotIndex;
    }

    internal void TestForceLatestSlotWriting()
    {
        lock (_gate)
        {
            if (_latestSlotIndex is not int latestIndex)
                return;

            _slots[latestIndex].State = GpuFrameSlotState.Writing;
        }
    }

    private void PublishLatestLocked(int newLatestIndex)
    {
        var oldLatest = _latestSlotIndex;
        _latestSlotIndex = newLatestIndex;

        if (oldLatest is int oldIndex && oldIndex != newLatestIndex)
            TryTransitionReplacedLatestLocked(oldIndex);
    }

    private void TryTransitionReplacedLatestLocked(int slotIndex)
    {
        var entry = _slots[slotIndex];

        if (entry.RefCount > 0)
            return;

        if (entry.State == GpuFrameSlotState.DisposePending)
            DestroySlotResourceLocked(entry);
        else if (entry.State == GpuFrameSlotState.Published)
            entry.State = GpuFrameSlotState.Free;
    }

    private void OnSlotRetainCountZeroLocked(int slotIndex, SlotEntry entry)
    {
        if (_latestSlotIndex == slotIndex)
        {
            if (entry.State == GpuFrameSlotState.DisposePending)
                DestroySlotResourceLocked(entry);

            return;
        }

        if (entry.State == GpuFrameSlotState.DisposePending)
            DestroySlotResourceLocked(entry);
        else if (entry.State == GpuFrameSlotState.Published)
            entry.State = GpuFrameSlotState.Free;
    }

    private void FinalizeSlotOnStopLocked(SlotEntry entry)
    {
        if (entry.RefCount == 0)
        {
            if (!ReusePhysicalResources)
                DestroySlotResourceLocked(entry);

            return;
        }

        if (entry.State is GpuFrameSlotState.Published or GpuFrameSlotState.Free or GpuFrameSlotState.Writing)
            entry.State = GpuFrameSlotState.DisposePending;
    }

    private void FinalizeSlotOnDisposeLocked(SlotEntry entry)
    {
        if (entry.RefCount > 0)
        {
            if (entry.State is GpuFrameSlotState.Published or GpuFrameSlotState.Free or GpuFrameSlotState.Writing)
                entry.State = GpuFrameSlotState.DisposePending;

            return;
        }

        DestroySlotResourceLocked(entry, forceDisposePhysical: true);
    }

    private void TryCompleteFinalizeLocked()
    {
        if (!_finalizeRequested || _disposed)
            return;

        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].RefCount > 0)
                return;
        }

        for (var i = 0; i < _slots.Length; i++)
        {
            var entry = _slots[i];

            if (entry.ResourceDisposed)
                continue;

            DestroySlotResourceLocked(entry, forceDisposePhysical: true);
        }

        _disposed = true;
    }

    private void DestroySlotResourceLocked(
        SlotEntry entry,
        bool reusePhysicalResources,
        bool forceDisposePhysical = false)
    {
        if (!reusePhysicalResources)
        {
            if (entry.Handle is IDisposable disposable && !entry.ResourceDisposed)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    _onResourceDisposeFailed?.Invoke(ex, entry.SlotIndex);
                }
            }

            entry.Handle = null;
            entry.ResourceDisposed = true;
        }
        else if (forceDisposePhysical)
        {
            entry.ResourceDisposed = true;
        }

        entry.State = GpuFrameSlotState.Free;
    }

    private void DestroySlotResourceLocked(SlotEntry entry, bool forceDisposePhysical = false) =>
        DestroySlotResourceLocked(entry, ReusePhysicalResources, forceDisposePhysical);

    private GpuFrameSlotLease CreateLeaseLocked(int slotIndex, SlotEntry entry)
    {
        var handle = entry.Handle ?? throw new InvalidOperationException("Published slot has no handle.");

        var frame = new GpuFrameReference
        {
            Backend = handle.Backend,
            Handle = handle,
            TextureSize = default,
            LogicalSize = default,
            FrameNumber = entry.FrameNumber,
            Timestamp = entry.TimestampTicks > 0
                ? MediaTime.FromStopwatchTicks(entry.TimestampTicks)
                : MediaTime.Zero
        };

        return new GpuFrameSlotLease(this, slotIndex, entry.Generation, frame);
    }

    private void ReportGenerationMismatch(int slotIndex, long expectedGeneration, long actualGeneration)
    {
        Interlocked.Increment(ref _generationMismatches);

#if DEBUG
        throw new InvalidOperationException(
            $"Generation mismatch on slot {slotIndex}: lease had {expectedGeneration}, slot has {actualGeneration}.");
#else
        _ = slotIndex;
        _ = expectedGeneration;
        _ = actualGeneration;
#endif
    }

    private sealed class SlotEntry
    {
        public int SlotIndex;

        public GpuFrameSlotState State = GpuFrameSlotState.Free;

        public long Generation;

        public int RefCount;

        public IGpuFrameHandle? Handle;

        public long FrameNumber;

        public long TimestampTicks;

        public long ContentToken;

        public bool ResourceDisposed;
    }
}
