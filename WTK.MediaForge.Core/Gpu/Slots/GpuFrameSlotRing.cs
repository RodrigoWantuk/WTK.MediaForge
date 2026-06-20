using WTK.MediaForge.Core.Frames;

namespace WTK.MediaForge.Core.Gpu.Slots;

public sealed class GpuFrameSlotRing : IDisposable
{
    private readonly object _gate = new();
    private readonly SlotEntry[] _slots;
    private int? _latestSlotIndex;
    private bool _stopped;
    private bool _disposed;
    private long _droppedFrames;
    private long _generationMismatches;

    public GpuFrameSlotRing(int slotCount = 3)
    {
        if (slotCount < 1)
            throw new ArgumentOutOfRangeException(nameof(slotCount));

        SlotCount = slotCount;
        _slots = new SlotEntry[slotCount];
        for (var i = 0; i < slotCount; i++)
            _slots[i] = new SlotEntry();
    }

    public int SlotCount { get; }

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

    public bool TryBeginWrite(out int slotIndex)
    {
        slotIndex = -1;

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_stopped)
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

    public void CompleteWrite(int slotIndex, IGpuFrameHandle handle, long frameNumber = 0)
    {
        ArgumentNullException.ThrowIfNull(handle);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_stopped)
                throw new InvalidOperationException("Ring is stopped.");

            var entry = _slots[slotIndex];

            if (entry.State != GpuFrameSlotState.Writing)
                throw new InvalidOperationException($"Slot {slotIndex} is not in Writing state.");

            entry.Generation++;
            entry.ContentToken++;
            entry.Handle = handle;
            entry.FrameNumber = frameNumber;
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

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _stopped = true;
            _latestSlotIndex = null;

            for (var i = 0; i < _slots.Length; i++)
                FinalizeSlotOnStopLocked(_slots[i]);
        }
    }

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
        }
    }

    internal int GetRefCount(int slotIndex)
    {
        lock (_gate)
            return _slots[slotIndex].RefCount;
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
            DestroySlotResourceLocked(entry);
            return;
        }

        if (entry.State is GpuFrameSlotState.Published or GpuFrameSlotState.Free)
            entry.State = GpuFrameSlotState.DisposePending;
    }

    private static void DestroySlotResourceLocked(SlotEntry entry)
    {
        entry.Handle = null;
        entry.ResourceDisposed = true;
        entry.State = GpuFrameSlotState.Free;
    }

    private GpuFrameSlotLease CreateLeaseLocked(int slotIndex, SlotEntry entry)
    {
        var handle = entry.Handle ?? throw new InvalidOperationException("Published slot has no handle.");

        var frame = new GpuFrameReference
        {
            Backend = handle.Backend,
            Handle = handle,
            TextureSize = default,
            LogicalSize = default,
            FrameNumber = entry.FrameNumber
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
        public GpuFrameSlotState State = GpuFrameSlotState.Free;

        public long Generation;

        public int RefCount;

        public IGpuFrameHandle? Handle;

        public long FrameNumber;

        public long ContentToken;

        public bool ResourceDisposed;
    }
}
