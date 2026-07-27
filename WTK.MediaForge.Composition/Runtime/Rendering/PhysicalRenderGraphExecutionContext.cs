namespace WTK.MediaForge.Composition.Runtime.Rendering;

/// <summary>
/// Tracks the portable ownership of resources produced by one physical RenderGraph submission.
/// The backend owns disposal: this context only returns resources at the exact physical point at
/// which they are no longer reachable from another operation in the immutable plan.
/// </summary>
internal sealed class PhysicalRenderGraphExecutionContext<TResource>
    where TResource : class
{
    private readonly IReadOnlyDictionary<string, OperationState> _operations;
    private int _publishedResources;
    private int _releasedResources;
    private int _abortedResources;
    private int _highWaterMark;

    public PhysicalRenderGraphExecutionContext(PhysicalRenderGraphPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        _operations = CreateOperationStates(plan.Operations);
    }

    public PhysicalRenderGraphExecutionMetrics Metrics => new(
        PublishedResources: _publishedResources,
        ReleasedResources: _releasedResources,
        RetainedResources: _publishedResources - _releasedResources,
        HighWaterMark: _highWaterMark,
        CompletedOperations: _operations.Values.Count(static state => state.IsCompleted),
        AbortedResources: _abortedResources);

    public bool HasReturnedToBaseline => Metrics.RetainedResources == 0;

    public void Publish(string operationKey, TResource resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        ArgumentNullException.ThrowIfNull(resource);

        var state = GetOperation(operationKey);
        if (state.Resource is not null)
        {
            throw new InvalidOperationException(
                $"Physical RenderGraph operation '{operationKey}' attempted to publish more than one resource.");
        }

        if (state.IsCompleted)
        {
            throw new InvalidOperationException(
                $"Physical RenderGraph operation '{operationKey}' completed before publishing its resource.");
        }

        state.Resource = resource;
        _publishedResources++;
        _highWaterMark = Math.Max(_highWaterMark, _publishedResources - _releasedResources);
    }

    public TResource GetRequiredDependency(string operationKey, string dependencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(dependencyKey);

        var operation = GetOperation(operationKey);
        if (!operation.Definition.Dependencies.Contains(dependencyKey, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Physical RenderGraph operation '{operationKey}' does not declare '{dependencyKey}' as a dependency.");
        }

        var dependency = GetOperation(dependencyKey);
        if (dependency.Resource is null || dependency.IsReleased)
        {
            throw new InvalidOperationException(
                $"Physical RenderGraph dependency '{dependencyKey}' is not available to '{operationKey}'.");
        }

        return dependency.Resource;
    }

    /// <summary>
    /// Marks an operation complete after its GPU work is known to be complete. Returned resources
    /// have no remaining physical consumers and must be disposed by the caller.
    /// </summary>
    public IReadOnlyList<TResource> CompleteOperation(string operationKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);

        var operation = GetOperation(operationKey);
        if (operation.IsCompleted)
        {
            throw new InvalidOperationException(
                $"Physical RenderGraph operation '{operationKey}' completed more than once.");
        }

        foreach (var dependencyKey in operation.Definition.Dependencies)
        {
            var dependency = GetOperation(dependencyKey);
            if (dependency.Resource is null || dependency.IsReleased)
            {
                throw new InvalidOperationException(
                    $"Physical RenderGraph operation '{operationKey}' completed without an available dependency '{dependencyKey}'.");
            }
        }

        operation.IsCompleted = true;
        var released = new List<TResource>();
        ReleaseIfEligible(operation, released);

        foreach (var dependencyKey in operation.Definition.Dependencies)
        {
            var dependency = GetOperation(dependencyKey);
            dependency.RemainingConsumers--;
            if (dependency.RemainingConsumers < 0)
            {
                throw new InvalidOperationException(
                    $"Physical RenderGraph operation '{dependencyKey}' consumed more times than planned.");
            }

            ReleaseIfEligible(dependency, released);
        }

        return released;
    }

    /// <summary>
    /// Returns every published, still-owned resource in reverse physical order. The caller must
    /// first wait for or abandon the submission according to the backend lifetime contract.
    /// </summary>
    public IReadOnlyList<TResource> Abort()
    {
        var released = new List<TResource>();
        foreach (var operation in _operations.Values.OrderByDescending(static state => state.Index))
        {
            if (operation.Resource is null || operation.IsReleased)
                continue;

            operation.IsReleased = true;
            _releasedResources++;
            _abortedResources++;
            released.Add(operation.Resource);
        }

        return released;
    }

    private void ReleaseIfEligible(OperationState operation, ICollection<TResource> released)
    {
        if (operation.Resource is null || operation.IsReleased || !operation.IsCompleted || operation.RemainingConsumers != 0)
            return;

        operation.IsReleased = true;
        _releasedResources++;
        released.Add(operation.Resource);
    }

    private OperationState GetOperation(string operationKey) =>
        _operations.TryGetValue(operationKey, out var operation)
            ? operation
            : throw new InvalidOperationException($"Physical RenderGraph operation '{operationKey}' is not present in this execution context.");

    private static IReadOnlyDictionary<string, OperationState> CreateOperationStates(
        IReadOnlyList<PhysicalRenderGraphOperation> operations)
    {
        var states = new Dictionary<string, OperationState>(StringComparer.Ordinal);
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            if (string.IsNullOrWhiteSpace(operation.Key))
                throw new InvalidOperationException("Physical RenderGraph operations must have a stable key.");
            if (!states.TryAdd(operation.Key, new OperationState(operation, index)))
                throw new InvalidOperationException($"Physical RenderGraph contains duplicate operation key '{operation.Key}'.");
            if (operation.Dependencies.Distinct(StringComparer.Ordinal).Count() != operation.Dependencies.Count)
                throw new InvalidOperationException($"Physical RenderGraph operation '{operation.Key}' has duplicate dependencies.");
            if (operation.Consumers.Distinct(StringComparer.Ordinal).Count() != operation.Consumers.Count)
                throw new InvalidOperationException($"Physical RenderGraph operation '{operation.Key}' has duplicate consumers.");
        }

        foreach (var state in states.Values)
        {
            foreach (var dependencyKey in state.Definition.Dependencies)
            {
                if (!states.TryGetValue(dependencyKey, out var dependency))
                    throw new InvalidOperationException($"Physical RenderGraph operation '{state.Definition.Key}' depends on missing operation '{dependencyKey}'.");
                if (dependency.Index >= state.Index)
                    throw new InvalidOperationException($"Physical RenderGraph operation '{state.Definition.Key}' is not topologically ordered after dependency '{dependencyKey}'.");
                if (!dependency.Definition.Consumers.Contains(state.Definition.Key, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Physical RenderGraph dependency '{dependencyKey}' does not declare '{state.Definition.Key}' as a consumer.");
            }

            foreach (var consumerKey in state.Definition.Consumers)
            {
                if (!states.TryGetValue(consumerKey, out var consumer))
                    throw new InvalidOperationException($"Physical RenderGraph operation '{state.Definition.Key}' references missing consumer '{consumerKey}'.");
                if (!consumer.Definition.Dependencies.Contains(state.Definition.Key, StringComparer.Ordinal))
                    throw new InvalidOperationException($"Physical RenderGraph consumer '{consumerKey}' does not declare '{state.Definition.Key}' as a dependency.");
            }
        }

        return states;
    }

    private sealed class OperationState(PhysicalRenderGraphOperation definition, int index)
    {
        public PhysicalRenderGraphOperation Definition { get; } = definition;

        public int Index { get; } = index;

        public int RemainingConsumers { get; set; } = definition.Consumers.Count;

        public TResource? Resource { get; set; }

        public bool IsCompleted { get; set; }

        public bool IsReleased { get; set; }
    }
}

internal sealed record PhysicalRenderGraphExecutionMetrics(
    int PublishedResources,
    int ReleasedResources,
    int RetainedResources,
    int HighWaterMark,
    int CompletedOperations,
    int AbortedResources);
