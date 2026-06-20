using System.Text.Json.Serialization;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Core.Color;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using WTK.MediaForge.Core.Media;

namespace WTK.MediaForge.Composition.DrawObjects;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(SourceLayerDrawObject), "source.layer")]
[JsonDerivedType(typeof(TextDrawObject), "text")]
[JsonDerivedType(typeof(SolidDrawObject), "solid")]
[JsonDerivedType(typeof(CanvasDrawObject), "canvas")]
public abstract class MediaForgeDrawObject
{
    public DrawObjectId Id { get; set; } = DrawObjectId.New();

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public Transform2D Transform { get; set; } = Transform2D.Default;

    public NormalizedRect? Crop { get; set; }

    public float Opacity { get; set; } = 1f;

    public BlendMode BlendMode { get; set; } = BlendMode.Normal;

    public List<MediaForgeEffect> Effects { get; set; } = [];
}
