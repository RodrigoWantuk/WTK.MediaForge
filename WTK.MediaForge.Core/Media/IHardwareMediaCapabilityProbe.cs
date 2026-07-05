namespace WTK.MediaForge.Core.Media;

public interface IHardwareMediaCapabilityProbe
{
    ValueTask<HardwareMediaCapabilityReport> ProbeAsync(CancellationToken cancellationToken = default);
}
