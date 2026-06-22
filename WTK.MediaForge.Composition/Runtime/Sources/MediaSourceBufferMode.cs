namespace WTK.MediaForge.Composition.Runtime.Sources;

internal enum MediaSourceBufferMode
{
    KeepLatest = 0,
    Queue = 1,
    TimelineDriven = 2,
    Static = 3
}
