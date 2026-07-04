namespace WTK.MediaForge.Studio.Models;

public sealed record StudioSelectionState(
    StudioSelectionKind Kind,
    string EntityId,
    string DisplayName,
    string TypeId,
    string Metadata = "",
    string Detail = "",
    string Destination = "",
    string Codec = "",
    string Bitrate = "",
    string Secret = "")
{
    public static StudioSelectionState None { get; } = new(
        StudioSelectionKind.None,
        string.Empty,
        "Nothing selected",
        string.Empty);
}
