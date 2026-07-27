using WTK.MediaForge.Composition.DrawObjects;
using WTK.MediaForge.Audio;
using WTK.MediaForge.Composition.Effects;
using WTK.MediaForge.Composition.Project;
using WTK.MediaForge.Composition.Runtime.Rendering;
using WTK.MediaForge.Composition.Scenes.Editing;
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
    public void Json_round_trip_preserves_portable_audio_graph()
    {
        var sourceId = AudioSourceId.New();
        var nodeId = AudioNodeId.New();
        var busId = AudioBusId.New();
        var sinkId = AudioSinkId.New();
        var project = new MediaForgeProject
        {
            Audio = new AudioGraphDefinition
            {
                Sources = [new AudioSourceDefinition { Id = sourceId, Name = "Tone", Kind = AudioSourceKind.GeneratedTone }],
                Nodes = [new AudioNodeDefinition { Id = nodeId, Name = "Gain", Kind = AudioNodeKind.Gain }],
                Connections = [new AudioConnection { SourceId = sourceId, ToNodeId = nodeId }],
                Buses = [new AudioBusDefinition { Id = busId, Name = "Program", InputNodeIds = [nodeId] }],
                Sinks = [new AudioSinkDefinition { Id = sinkId, Name = "Program", Kind = AudioSinkKind.ProgramMix }],
                OutputRoutes = [new AudioOutputRoute { Id = AudioOutputRouteId.New(), BusId = busId, SinkId = sinkId }]
            }
        };

        var restored = MediaForgeProjectSerializer.Deserialize(MediaForgeProjectSerializer.Serialize(project));

        Assert.Equal(sourceId, Assert.Single(restored.Audio.Sources).Id);
        Assert.Equal(nodeId, Assert.Single(restored.Audio.Nodes).Id);
        Assert.Equal(busId, Assert.Single(restored.Audio.Buses).Id);
        Assert.Equal(sinkId, Assert.Single(restored.Audio.Sinks).Id);
    }

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
    public void Capability_registry_describes_execution_contracts_for_every_effect()
    {
        var registry = EffectCapabilityRegistry.Default;

        var chroma = registry.GetRequired(new ChromaKeyEffect());
        Assert.True(chroma.AcceptsScope(EffectScope.Source));
        Assert.True(chroma.AcceptsScope(EffectScope.Layer));
        Assert.Equal(EffectAlphaBehavior.Modifies, chroma.AlphaBehavior);
        Assert.False(chroma.IsTemporal);
        Assert.True(chroma.SupportsMask);

        var blur = registry.GetRequired(new BlurEffect());
        Assert.False(blur.AcceptsScope(EffectScope.Source));
        Assert.Equal(EffectPassClass.Spatial, blur.PassClass);
    }

    [Fact]
    public void Mask_capabilities_do_not_overstate_serializable_models_as_executable()
    {
        var registry = EffectMaskCapabilityRegistry.Default;

        var rectangle = registry.GetRequired(new RectangleEffectMask());
        Assert.True(rectangle.ModelSupported);
        Assert.True(rectangle.RuntimeSupported);
        Assert.True(rectangle.GpuBackendSupported);
        Assert.True(rectangle.ProductAvailable);
        Assert.False(rectangle.StudioEditable);
        Assert.False(rectangle.TransformSupported);

        var image = registry.GetRequired(new ImageAlphaEffectMask());
        Assert.True(image.ModelSupported);
        Assert.False(image.RuntimeSupported);
        Assert.False(image.GpuBackendSupported);
        Assert.False(image.StudioEditable);
        Assert.False(image.ProductAvailable);
        Assert.False(string.IsNullOrWhiteSpace(image.UnavailableReason));
    }

    [Fact]
    public void Validator_rejects_enabled_mask_without_an_executable_runtime()
    {
        var project = ProjectWithChromaEffect();
        Assert.IsType<ChromaKeyEffect>(project.Canvases[0].Objects[0].Effects[0]).Mask = new ImageAlphaEffectMask
        {
            AssetPath = "assets/masks/matte.png"
        };

        var validation = MediaForgeProjectValidator.Validate(project);

        Assert.Contains(validation.Issues, issue => issue.Code == "effect.mask.unavailable");
    }

    [Fact]
    public void Validator_rejects_invalid_effect_scope_before_rendering()
    {
        var project = ProjectWithChromaEffect();
        project.SourceDefinitions.Add(new MediaForgeSourceDefinition
        {
            Name = "Camera",
            Effects = [new BlurEffect { Name = "Invalid source blur" }]
        });
        project.Canvases[0].Effects.Add(new ChromaKeyEffect { Name = "Invalid canvas key" });

        var validation = MediaForgeProjectValidator.Validate(project);

        Assert.Equal(2, validation.Issues.Count(issue => issue.Code == "effect.scope.invalid"));
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

    [Fact]
    public void Json_round_trip_and_snapshot_preserve_image_alpha_mask_configuration()
    {
        var project = ProjectWithChromaEffect();
        var effect = Assert.IsType<ChromaKeyEffect>(project.Canvases[0].Objects[0].Effects[0]);
        effect.Mask = new ImageAlphaEffectMask
        {
            AssetPath = "assets/masks/soft-edge.png",
            Feather = 0.25f,
            Invert = true,
            Bounds = new NormalizedRect(0.1f, 0.2f, 0.8f, 0.9f),
            Transform = new Transform2D { Size = new CanvasSize(320, 180) }
        };

        var restored = MediaForgeProjectSerializer.Deserialize(MediaForgeProjectSerializer.Serialize(project));
        var restoredEffect = Assert.IsType<ChromaKeyEffect>(restored.Canvases[0].Objects[0].Effects[0]);
        var restoredMask = Assert.IsType<ImageAlphaEffectMask>(restoredEffect.Mask);
        Assert.Equal("assets/masks/soft-edge.png", restoredMask.AssetPath);
        Assert.True(restoredMask.Invert);
        Assert.Equal(0.25f, restoredMask.Feather);

        var snapshot = ProjectStateSnapshotFactory.CreateImmutableSnapshot(restored);
        var snapshotEffect = Assert.IsType<ChromaKeyEffectSnapshot>(snapshot.Canvases[0].Objects[0].Effects[0]);
        var snapshotMask = Assert.IsType<ImageAlphaEffectMaskStateSnapshot>(snapshotEffect.Mask);
        Assert.Equal(restoredMask.AssetPath, snapshotMask.AssetPath);
    }

    [Fact]
    public void Validator_rejects_invalid_mask_geometry_and_missing_image_asset()
    {
        var project = ProjectWithChromaEffect();
        var effect = Assert.IsType<ChromaKeyEffect>(project.Canvases[0].Objects[0].Effects[0]);
        effect.Mask = new ImageAlphaEffectMask
        {
            Feather = 2f,
            Bounds = new NormalizedRect(0.8f, 0.2f, 0.1f, 0.9f),
            Transform = new Transform2D { Size = CanvasSize.Empty }
        };

        var validation = MediaForgeProjectValidator.Validate(project);

        Assert.Contains(validation.Issues, issue => issue.Code == "effect.mask.feather");
        Assert.Contains(validation.Issues, issue => issue.Code == "effect.mask.bounds");
        Assert.Contains(validation.Issues, issue => issue.Code == "effect.mask.transform");
        Assert.Contains(validation.Issues, issue => issue.Code == "effect.mask.asset_path");
    }

    [Fact]
    public void Gradient_and_luma_masks_round_trip_snapshot_and_fingerprint_with_all_coverage_properties()
    {
        var project = ProjectWithChromaEffect();
        var effect = Assert.IsType<ChromaKeyEffect>(project.Canvases[0].Objects[0].Effects[0]);
        effect.Mask = new GradientEffectMask
        {
            CoordinateSpace = EffectMaskCoordinateSpace.Canvas,
            Opacity = 0.65f,
            Feather = 0.15f,
            Start = new NormalizedPoint(0.1f, 0.2f),
            End = new NormalizedPoint(0.8f, 0.9f),
            StartOpacity = 0.9f,
            EndOpacity = 0.2f
        };

        var restored = MediaForgeProjectSerializer.Deserialize(MediaForgeProjectSerializer.Serialize(project));
        var restoredMask = Assert.IsType<GradientEffectMask>(
            Assert.IsType<ChromaKeyEffect>(restored.Canvases[0].Objects[0].Effects[0]).Mask);
        Assert.Equal(EffectMaskCoordinateSpace.Canvas, restoredMask.CoordinateSpace);
        Assert.Equal(0.65f, restoredMask.Opacity);

        var snapshot = ProjectStateSnapshotFactory.CreateImmutableSnapshot(restored);
        var snapshotMask = Assert.IsType<GradientEffectMaskStateSnapshot>(
            Assert.IsType<ChromaKeyEffectSnapshot>(snapshot.Canvases[0].Objects[0].Effects[0]).Mask);
        Assert.Equal(0.9f, snapshotMask.StartOpacity);
        Assert.Contains("mask.type=gradient", EffectStateFingerprint.CreateSemanticConfiguration(
            Assert.IsType<ChromaKeyEffectSnapshot>(snapshot.Canvases[0].Objects[0].Effects[0])));

        effect.Mask = new LumaEffectMask { AssetPath = "assets/masks/luma.png" };
        var luma = Assert.IsType<LumaEffectMask>(
            Assert.IsType<ChromaKeyEffect>(MediaForgeProjectSerializer.Deserialize(MediaForgeProjectSerializer.Serialize(project))
                .Canvases[0].Objects[0].Effects[0]).Mask);
        Assert.Equal("assets/masks/luma.png", luma.AssetPath);
    }

    [Fact]
    public void Validator_rejects_invalid_gradient_mask_configuration()
    {
        var project = ProjectWithChromaEffect();
        Assert.IsType<ChromaKeyEffect>(project.Canvases[0].Objects[0].Effects[0]).Mask = new GradientEffectMask
        {
            Opacity = 2f,
            Start = new NormalizedPoint(-0.1f, 0f),
            EndOpacity = -1f
        };

        var validation = MediaForgeProjectValidator.Validate(project);

        Assert.Contains(validation.Issues, issue => issue.Code == "effect.mask.opacity");
        Assert.Contains(validation.Issues, issue => issue.Code == "effect.mask.gradient");
    }

    [Fact]
    public void Adjustment_layer_is_serialized_snapshotted_and_compiled_as_a_layers_below_checkpoint()
    {
        var builder = MediaForgeProjectBuilder.Create()
            .Scene("Program", 1920, 1080, out var canvas)
            .DesktopSource("Desktop", displayIndex: 0, out var source)
            .AddSourceLayer(canvas, source)
            .AddAdjustmentLayer(canvas, layer =>
            {
                layer.Effects.Add(new BlurEffect { Radius = 8f });
                layer.Mask = new RoundedRectangleEffectMask { CornerRadius = 0.2f, Feather = 0.1f };
            })
            .AddText(canvas, "Above adjustment")
            .OffscreenOutput("Program", canvas, 1920, 1080, out _);
        var project = builder.BuildValidated();

        var restored = MediaForgeProjectSerializer.Deserialize(MediaForgeProjectSerializer.Serialize(project));
        var adjustment = Assert.IsType<AdjustmentLayerDrawObject>(restored.Canvases[0].Objects[1]);
        Assert.IsType<RoundedRectangleEffectMask>(adjustment.Mask);

        var snapshot = ProjectStateSnapshotFactory.CreateImmutableSnapshot(restored);
        var adjustmentSnapshot = Assert.IsType<AdjustmentLayerDrawObjectSnapshot>(snapshot.Canvases[0].Objects[1]);
        Assert.Equal(AdjustmentLayerTargetMode.LayersBelow, adjustmentSnapshot.TargetMode);
        Assert.IsType<RoundedRectangleEffectMaskStateSnapshot>(adjustmentSnapshot.Mask);

        var graph = MediaForgeRenderGraphCompiler.Compile(restored);
        var checkpoint = Assert.Single(graph.Nodes,
            node => node.Kind == MediaForgeRenderGraphNodeKind.AdjustmentLayerCheckpoint);
        var canvasRender = Assert.Single(graph.Nodes,
            node => node.Kind == MediaForgeRenderGraphNodeKind.CanvasRender);
        Assert.Single(checkpoint.Dependencies);
        Assert.Contains(checkpoint.Key, canvasRender.Dependencies);
        Assert.Contains(canvasRender.Dependencies, key => key.StartsWith("primitive:", StringComparison.Ordinal));
    }

    [Fact]
    public void Adjustment_layer_mask_patch_clones_payload_and_only_targets_adjustment_layers()
    {
        var project = new MediaForgeProject
        {
            Canvases =
            [
                new MediaForgeCanvas
                {
                    Objects = [new AdjustmentLayerDrawObject { Transform = new Transform2D { Size = new CanvasSize(100, 100) } }]
                }
            ]
        };
        var layer = Assert.IsType<AdjustmentLayerDrawObject>(project.Canvases[0].Objects[0]);
        var mask = new RectangleEffectMask { Opacity = 0.4f };

        SceneMutationPatchApplier.Apply(
            project,
            project.Canvases[0].Id,
            new SceneMutationPatch.SetAdjustmentLayerMask(layer.Id, mask));

        var stored = Assert.IsType<RectangleEffectMask>(layer.Mask);
        Assert.NotSame(mask, stored);
        Assert.Equal(0.4f, stored.Opacity);
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
