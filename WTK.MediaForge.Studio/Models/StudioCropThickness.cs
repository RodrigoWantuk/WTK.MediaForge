namespace WTK.MediaForge.Studio.Models;

public readonly record struct StudioCropThickness(double Left, double Top, double Right, double Bottom)
{
    public override string ToString() => $"{Left:0} / {Top:0} / {Right:0} / {Bottom:0}";
}
