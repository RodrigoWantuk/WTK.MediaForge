using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Serialization;
using WTK.MediaForge.Composition.Snapshots;
using WTK.MediaForge.Composition.Validation;
using WTK.MediaForge.Core.Frames;
using WTK.MediaForge.Core.Geometry;
using WTK.MediaForge.Core.Identifiers;
using Xunit;

namespace WTK.MediaForge.Composition.Tests;

public class EffectModelTests
{
    [Fact]
    public void Json_round_trip_preserves_effect_discriminator()
    {
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Objects =
                    [
                        new SourceLayerDrawObject
                        {
                            Transform = new Transform2D { Size = new CanvasSize(100, 100) },
                            Effects =
                            [
                                new ChromaKeyEffect
                                {
                                    Name = "Green screen",
                                    Order = 0,
                                    Similarity = 0.35f
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var json = MediaForgeProjectSerializer.Serialize(project);
        Assert.Contains("\"$type\": \"effect.chroma\"", json);

        var restored = MediaForgeProjectSerializer.Deserialize(json);
        var layer = Assert.IsType<SourceLayerDrawObject>(restored.Canvases[0].Objects[0]);
        var effect = Assert.IsType<ChromaKeyEffect>(Assert.Single(layer.Effects));
        Assert.Equal(0.35f, effect.Similarity);
    }

    [Fact]
    public void Schema_v1_transition_effect_is_removed_by_explicit_project_migration()
    {
        var json = MediaForgeProjectSerializer.Serialize(ProjectWithChromaEffect())
            .Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1", StringComparison.Ordinal)
            .Replace("effect.chroma", "effect.transition", StringComparison.Ordinal);

        var restored = MediaForgeProjectSerializer.Deserialize(json);

        Assert.Equal(MediaForgeProject.CurrentSchemaVersion, restored.SchemaVersion);
        var layer = Assert.IsType<SourceLayerDrawObject>(restored.Canvases[0].Objects[0]);
        Assert.Empty(layer.Effects);
    }

    [Fact]
    public void Current_project_rejects_retired_transition_effect_discriminator()
    {
        var json = MediaForgeProjectSerializer.Serialize(ProjectWithChromaEffect())
            .Replace("effect.chroma", "effect.transition", StringComparison.Ordinal);

        var error = Assert.Throws<System.Text.Json.JsonException>(() => MediaForgeProjectSerializer.Deserialize(json));

        Assert.Contains("not an effect type", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validator_rejects_invalid_chroma_similarity()
    {
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Name = "Main",
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SourceLayerDrawObject
                        {
                            Name = "Layer",
                            Transform = new Transform2D { Size = new CanvasSize(100, 100) },
                            Effects =
                            [
                                new ChromaKeyEffect
                                {
                                    Name = "Key",
                                    Similarity = 1.5f
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var validation = MediaForgeProjectValidator.Validate(project);
        Assert.False(validation.IsValid);
        Assert.Contains(validation.Issues, i => i.Code == "effect.chroma.similarity");
    }

    [Fact]
    public void Snapshot_factory_deep_clones_effects()
    {
        var effectId = EffectId.New();
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Size = new FrameSize(1920, 1080),
                    Objects =
                    [
                        new SolidDrawObject
                        {
                            Transform = new Transform2D { Size = new CanvasSize(100, 100) },
                            Effects =
                            [
                                new BlurEffect
                                {
                                    Id = effectId,
                                    Name = "Soft",
                                    Radius = 8f
                                }
                            ]
                        }
                    ]
                }
            ]
        };

        var snapshot = ProjectStateSnapshotFactory.CreateImmutableSnapshot(project);
        var blur = Assert.IsType<BlurEffectSnapshot>(Assert.Single(snapshot.Canvases[0].Objects[0].Effects));
        Assert.Equal(effectId, blur.Id);
        Assert.Equal(8f, blur.Radius);
    }

    private static MediaForgeProject ProjectWithChromaEffect() => new()
    {
        Canvases =
        [
            new MediaForgeCanvas
            {
                Objects =
                [
                    new SourceLayerDrawObject
                    {
                        Transform = new Transform2D { Size = new CanvasSize(100, 100) },
                        Effects = [new ChromaKeyEffect { Name = "Legacy transition placeholder" }]
                    }
                ]
            }
        ]
    };
}
