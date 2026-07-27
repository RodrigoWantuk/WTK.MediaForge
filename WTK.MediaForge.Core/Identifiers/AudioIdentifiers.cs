namespace WTK.MediaForge.Core.Identifiers;

public readonly record struct AudioSourceId(Guid Value)
{
    public static AudioSourceId New() => new(Guid.NewGuid());
    public static AudioSourceId From(Guid value) => value == Guid.Empty ? throw new ArgumentException("AudioSourceId cannot be Guid.Empty.", nameof(value)) : new(value);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct AudioNodeId(Guid Value)
{
    public static AudioNodeId New() => new(Guid.NewGuid());
    public static AudioNodeId From(Guid value) => value == Guid.Empty ? throw new ArgumentException("AudioNodeId cannot be Guid.Empty.", nameof(value)) : new(value);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct AudioBusId(Guid Value)
{
    public static AudioBusId New() => new(Guid.NewGuid());
    public static AudioBusId From(Guid value) => value == Guid.Empty ? throw new ArgumentException("AudioBusId cannot be Guid.Empty.", nameof(value)) : new(value);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct AudioOutputRouteId(Guid Value)
{
    public static AudioOutputRouteId New() => new(Guid.NewGuid());
    public static AudioOutputRouteId From(Guid value) => value == Guid.Empty ? throw new ArgumentException("AudioOutputRouteId cannot be Guid.Empty.", nameof(value)) : new(value);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}

public readonly record struct AudioSinkId(Guid Value)
{
    public static AudioSinkId New() => new(Guid.NewGuid());
    public static AudioSinkId From(Guid value) => value == Guid.Empty ? throw new ArgumentException("AudioSinkId cannot be Guid.Empty.", nameof(value)) : new(value);
    public bool IsEmpty => Value == Guid.Empty;
    public override string ToString() => Value.ToString();
}
