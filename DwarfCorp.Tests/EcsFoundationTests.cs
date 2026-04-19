using Arch.Core;
using DwarfCorp.ECS;
using DwarfCorp.ECS.Components;
using DwarfCorp.Infrastructure;
using DwarfCorp.Saving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using Xunit;

namespace DwarfCorp.Tests;

/// <summary>
/// L.4 foundation tests. Pin the contract of the Arch setup, the save-format
/// versioning, and the migration-shim shape BEFORE we start moving real component
/// families over — so every subsequent subsystem migration has a tripwire it can
/// break against.
/// </summary>
public class EcsFoundationTests
{
    [Fact]
    public void EcsWorld_ResolvesFromContainer_AsSingleton()
    {
        Services.Initialize();
        var a = Services.Provider.GetRequiredService<EcsWorld>();
        var b = Services.Provider.GetRequiredService<EcsWorld>();
        Assert.NotNull(a);
        Assert.Same(a, b); // DI singleton — both lookups hit the same instance.
        Assert.NotNull(a.World);
    }

    [Fact]
    public void ArchWorld_CanCreateEntityAndAttachTransform()
    {
        Services.Initialize();
        var ecs = Services.Provider.GetRequiredService<EcsWorld>();
        var world = ecs.World;
        int initialSize = world.Size;

        var entity = world.Create(new Transform
        {
            Position = new Vector3(1, 2, 3),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One,
        });

        Assert.True(world.IsAlive(entity));
        Assert.Equal(initialSize + 1, world.Size);

        ref var t = ref world.Get<Transform>(entity);
        Assert.Equal(new Vector3(1, 2, 3), t.Position);

        world.Destroy(entity);
        Assert.False(world.IsAlive(entity));
    }

    [Fact]
    public void SaveFormatVersion_DefaultsToV1_WhenFieldMissingInLegacyJson()
    {
        // Every existing save on every user's disk is from before we added this field.
        // Newtonsoft has to deserialize those into SaveFormatVersion = 1 so the loader
        // can branch on the legacy path without any user interaction.
        const string legacyMetaJson =
            """
            {
              "TimeOfDay": 0.5,
              "Version": "21.04.03_FNA",
              "Commit": "abcdef1",
              "DescriptionString": "Description"
            }
            """;

        var meta = JsonConvert.DeserializeObject<MetaData>(legacyMetaJson);
        Assert.NotNull(meta);
        Assert.Equal(1, meta.SaveFormatVersion);
    }

    [Fact]
    public void SaveFormatVersion_RoundtripsThroughJson()
    {
        var original = new MetaData { SaveFormatVersion = 2, Version = "v-test" };
        var json = JsonConvert.SerializeObject(original);
        Assert.Contains("\"SaveFormatVersion\":2", json);

        var parsed = JsonConvert.DeserializeObject<MetaData>(json);
        Assert.Equal(2, parsed.SaveFormatVersion);
    }

    [Fact]
    public void ComponentSaveMigration_OnNullLegacy_IsNoOpAndReturnsZero()
    {
        Services.Initialize();
        var ecs = Services.Provider.GetRequiredService<EcsWorld>();
        var log = Services.Provider.GetRequiredService<ILogger<ComponentSaveMigration>>();
        var shim = new ComponentSaveMigration(ecs, log);

        int created = shim.Migrate(null);
        Assert.Equal(0, created);
    }

    [Fact]
    public void ComponentSaveMigration_IsIdempotent_OnEmptyLegacy()
    {
        Services.Initialize();
        var ecs = Services.Provider.GetRequiredService<EcsWorld>();
        var log = Services.Provider.GetRequiredService<ILogger<ComponentSaveMigration>>();
        var shim = new ComponentSaveMigration(ecs, log);

        var empty = new ComponentManager.ComponentSaveData();
        int firstRun = shim.Migrate(empty);
        int secondRun = shim.Migrate(empty);
        Assert.Equal(firstRun, secondRun);
        // HasAny<Transform> is false right now because MigrateTransforms isn't
        // implemented yet — this assertion is the one that flips to `True` when
        // the first per-family migration lands.
        Assert.False(shim.HasAny<Transform>());
    }
}
