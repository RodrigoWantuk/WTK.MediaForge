namespace WTK.MediaForge.Composition.Outputs;

public enum RenderOutputSinkBackpressureMode
{
    DropNewest,
    DropOldest,
    KeepLatest,
    BlockProducer
}
