using System;
using System.Collections.Generic;
using System.Reflection;
using Arch.Core;
using DwarfCorp;
using DwarfCorp.ECS;
using DwarfCorp.Infrastructure;
using DwarfCorp.Saving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Xunit;

// Aliases to avoid colliding with the legacy DwarfCorp.* types of the same name.
using EcsPhysics = DwarfCorp.ECS.Components.Physics;
using EcsHealth = DwarfCorp.ECS.Components.Health;
using EcsTinter = DwarfCorp.ECS.Components.Tinter;
using EcsSprite = DwarfCorp.ECS.Components.Sprite;
using EcsAnimatedSprite = DwarfCorp.ECS.Components.AnimatedSprite;
using EcsInstanceMeshRef = DwarfCorp.ECS.Components.InstanceMeshRef;
using EcsInventory = DwarfCorp.ECS.Components.Inventory;
using EcsEquipment = DwarfCorp.ECS.Components.Equipment;
using EcsLightEmission = DwarfCorp.ECS.Components.LightEmission;
using EcsRadiusSensor = DwarfCorp.ECS.Components.RadiusSensor;
using EcsEnemySensor = DwarfCorp.ECS.Components.EnemySensor;
using EcsSpawnOnExplored = DwarfCorp.ECS.Components.SpawnOnExplored;
using EcsFlammable = DwarfCorp.ECS.Components.Flammable;
using EcsFire = DwarfCorp.ECS.Components.Fire;
using EcsFollower = DwarfCorp.ECS.Components.Follower;
using EcsBobber = DwarfCorp.ECS.Components.Bobber;

namespace DwarfCorp.Tests;

/// <summary>
/// L.4 commits #2..#11 — per-family migrations. Each family's test builds a
/// legacy fixture that includes the target GameComponent type as a child of a
/// root-level component (so Transform migration creates the entity first), runs
/// <see cref="ComponentSaveMigration.Migrate"/>, and asserts the corresponding
/// Arch component landed on the right entity with correct data.
/// </summary>
public class MigrateFamiliesTests
{
    // ────────────────────────── fixture helpers ──────────────────────────

    private static void SetField(object target, string name, object value)
    {
        var t = target.GetType();
        FieldInfo f = null;
        while (t != null && f == null)
        {
            f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            t = t.BaseType;
        }
        if (f == null) throw new InvalidOperationException($"Field {name} not found on {target.GetType()}");
        f.SetValue(target, value);
    }

    /// <summary>
    /// Stamp the GameComponent fields (GlobalID, ParentID, Name, LocalTransform) on
    /// a freshly Activator-constructed legacy object so the migration sees it as a
    /// normal deserialized instance.
    /// </summary>
    private static T Synth<T>(uint globalId, uint parent, Matrix? localTransform = null, string name = "test")
        where T : GameComponent
    {
        uint parentId = parent;
        // Some legacy constructors (e.g. Tinter field init `GameSettings.Current.EntityLighting`)
        // deref a null static at construction time, which blows up in unit test context
        // where GameSettings isn't loaded. FormatterServices.GetUninitializedObject builds
        // the same way Newtonsoft.Json does during save deserialization — skips the ctor,
        // field initializers don't run, fields start at default(T). That matches how these
        // objects actually arrive inside ComponentSaveData during a real load, so the
        // migration code is exercised with the same shape of input.
        var instance = (T)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(T));
        instance.Name = name;
        instance.GlobalID = globalId;
        SetField(instance, "ParentID", parentId);
        if (localTransform.HasValue)
            instance.LocalTransform = localTransform.Value;
        return instance;
    }

    private static GameComponent SynthRoot(uint id) => Synth<GameComponent>(id, 0, Matrix.Identity, "root");
    private static GameComponent SynthHost(uint id, uint parent) =>
        Synth<GameComponent>(id, parent, Matrix.CreateTranslation(1, 2, 3), "host");

    private static (EcsWorld ecs, ComponentSaveMigration shim) FreshShim()
    {
        Services.Initialize();
        var logFactory = Services.Provider.GetRequiredService<ILoggerFactory>();
        var ecs = new EcsWorld(logFactory.CreateLogger<EcsWorld>());
        return (ecs, new ComponentSaveMigration(ecs, logFactory.CreateLogger<ComponentSaveMigration>()));
    }

    private static ComponentManager.ComponentSaveData Save(uint rootId, params GameComponent[] components)
        => new()
        {
            RootComponent = rootId,
            SaveableComponents = new List<GameComponent>(components),
        };

    // ──────────────────────────── per-family tests ────────────────────────────

    [Fact]
    public void Physics_Migrates_ToEntityOfItsRootAncestor()
    {
        var (ecs, shim) = FreshShim();

        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        // Physics is also root-level (extends GameComponent directly). Put the
        // Physics AS the root-level host.
        var physics = Synth<DwarfCorp.Physics>(3, parent: 1, Matrix.CreateTranslation(5, 0, 0));
        physics.Velocity = new Vector3(7, 8, 9);
        physics.Mass = 4.5f;
        physics.Friction = 0.88f;

        shim.Migrate(Save(rootId: 1, root, host, physics));

        var entity = shim.LegacyIdToEntity[3];
        Assert.True(ecs.World.Has<EcsPhysics>(entity));
        ref var p = ref ecs.World.Get<EcsPhysics>(entity);
        Assert.Equal(new Vector3(7, 8, 9), p.Velocity);
        Assert.Equal(4.5f, p.Mass);
        Assert.Equal(0.88f, p.Friction);
    }

    [Fact]
    public void Health_ChildOfRoot_FoldsOntoRootEntity()
    {
        var (ecs, shim) = FreshShim();

        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var health = Synth<DwarfCorp.Health>(3, parent: 2);  // child of host, not of RootComponent
        health.Hp = 50f;
        health.MaxHealth = 100f;
        health.MinHealth = 0f;

        shim.Migrate(Save(rootId: 1, root, host, health));

        var hostEntity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsHealth>(hostEntity));
        ref var h = ref ecs.World.Get<EcsHealth>(hostEntity);
        Assert.Equal(50f, h.Hp);
        Assert.Equal(100f, h.MaxHealth);
    }

    [Fact]
    public void Tinter_ChildOfRoot_FoldsOntoRootEntity()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var sprite = Synth<DwarfCorp.SimpleSprite>(3, parent: 2);
        sprite.LightRamp = Color.Red;
        sprite.VertexColorTint = Color.Green;
        sprite.WorldWidth = 2.5f;
        sprite.WorldHeight = 3.0f;

        shim.Migrate(Save(rootId: 1, root, host, sprite));

        var entity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsTinter>(entity));
        Assert.True(ecs.World.Has<EcsSprite>(entity));
        Assert.Equal(Color.Red, ecs.World.Get<EcsTinter>(entity).LightRamp);
        Assert.Equal(2.5f, ecs.World.Get<EcsSprite>(entity).WorldWidth);
    }

    [Fact]
    public void LayeredSimpleSprite_Migrates_WithSilhouetteFlag()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var layered = Synth<DwarfCorp.LayeredSimpleSprite>(3, parent: 2);
        layered.DrawSilhouette = true;
        layered.SilhouetteColor = Color.Yellow;

        shim.Migrate(Save(rootId: 1, root, host, layered));

        var entity = shim.LegacyIdToEntity[2];
        ref var anim = ref ecs.World.Get<EcsAnimatedSprite>(entity);
        Assert.True(anim.DrawSilhouette);
        Assert.Equal(Color.Yellow, anim.SilhouetteColor);
    }

    [Fact]
    public void InstanceMesh_Migrates_ModelTypeString()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var mesh = Synth<DwarfCorp.InstanceMesh>(3, parent: 2);
        mesh.ModelType = "Barrel";

        shim.Migrate(Save(rootId: 1, root, host, mesh));

        var entity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsInstanceMeshRef>(entity));
        Assert.Equal("Barrel", ecs.World.Get<EcsInstanceMeshRef>(entity).ModelType);
    }

    [Fact]
    public void Inventory_Migrates_WithItemList()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var inv = Synth<DwarfCorp.Inventory>(3, parent: 2);
        // FormatterServices skips field initializers, so Resources starts null.
        // In a real JSON load Newtonsoft would populate this list; we set it up
        // manually for the test.
        inv.Resources = new List<DwarfCorp.Inventory.InventoryItem>();
        inv.Resources.Add(new DwarfCorp.Inventory.InventoryItem
        {
            Resource = null,  // deliberate — Resource type is complex, null is valid snapshot data
            MarkedForRestock = true,
            MarkedForUse = false,
        });

        shim.Migrate(Save(rootId: 1, root, host, inv));

        var entity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsInventory>(entity));
        var migratedList = ecs.World.Get<EcsInventory>(entity).Items;
        Assert.Single(migratedList);
        Assert.True(migratedList[0].MarkedForRestock);
    }

    [Fact]
    public void Equipment_Migrates_WithSlotMap()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var eq = Synth<DwarfCorp.Equipment>(3, parent: 2);
        eq.EquippedItems = new Dictionary<string, Resource>();
        eq.EquippedItems["Mainhand"] = null; // Resource is complex, null is fine for the shim snapshot

        shim.Migrate(Save(rootId: 1, root, host, eq));

        var entity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsEquipment>(entity));
        Assert.True(ecs.World.Get<EcsEquipment>(entity).EquippedItems.ContainsKey("Mainhand"));
    }

    [Fact]
    public void LightEmitter_Migrates_RangeAndIntensity()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var lamp = Synth<DwarfCorp.LightEmitter>(3, parent: 2);
        lamp.Light = new DynamicLight(range: 8, intensity: 0.7f, add: false);

        shim.Migrate(Save(rootId: 1, root, host, lamp));

        var entity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsLightEmission>(entity));
        ref var light = ref ecs.World.Get<EcsLightEmission>(entity);
        Assert.Equal(8f, light.Range);
        Assert.Equal(0.7f, light.Intensity);
    }

    [Fact]
    public void RadiusSensor_Migrates_Fields()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var sensor = Synth<DwarfCorp.RadiusSensor>(3, parent: 2);
        sensor.SenseRadius = 400f;
        sensor.CheckLineOfSight = false;

        shim.Migrate(Save(rootId: 1, root, host, sensor));

        var entity = shim.LegacyIdToEntity[2];
        ref var s = ref ecs.World.Get<EcsRadiusSensor>(entity);
        Assert.Equal(400f, s.SenseRadiusSquared);
        Assert.False(s.CheckLineOfSight);
    }

    [Fact]
    public void SpawnOnExploredTrigger_Migrates_EntityKey()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var trigger = Synth<DwarfCorp.SpawnOnExploredTrigger>(3, parent: 2);
        trigger.EntityToSpawn = "Goblin";
        trigger.SpawnLocation = new Vector3(10, 20, 30);

        shim.Migrate(Save(rootId: 1, root, host, trigger));

        var entity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsSpawnOnExplored>(entity));
        ref var s = ref ecs.World.Get<EcsSpawnOnExplored>(entity);
        Assert.Equal("Goblin", s.EntityToSpawn);
        Assert.Equal(new Vector3(10, 20, 30), s.SpawnLocation);
    }

    [Fact]
    public void Flammable_Migrates_HeatFlashpointDamage()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var f = Synth<DwarfCorp.Flammable>(3, parent: 2);
        f.Heat = 12.5f; f.Flashpoint = 50f; f.Damage = 2.5f;

        shim.Migrate(Save(rootId: 1, root, host, f));

        var entity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsFlammable>(entity));
        Assert.Equal(12.5f, ecs.World.Get<EcsFlammable>(entity).Heat);
    }

    [Fact]
    public void Follower_Migrates_RadiusAndTarget()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1); var host = SynthHost(2, 1);
        var f = Synth<DwarfCorp.Follower>(3, parent: 2);
        f.FollowRadius = 5f; f.FollowRate = 0.8f; f.TargetPos = new Vector3(10, 0, 10);
        shim.Migrate(Save(1, root, host, f));
        Assert.True(ecs.World.Has<EcsFollower>(shim.LegacyIdToEntity[2]));
        Assert.Equal(5f, ecs.World.Get<EcsFollower>(shim.LegacyIdToEntity[2]).FollowRadius);
    }

    [Fact]
    public void Bobber_Migrates_SineParams()
    {
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1); var host = SynthHost(2, 1);
        var b = Synth<DwarfCorp.Bobber>(3, parent: 2);
        b.Magnitude = 0.2f; b.Rate = 2f; b.Offset = 1f; b.OrigY = 5f;
        shim.Migrate(Save(1, root, host, b));
        var eb = ecs.World.Get<EcsBobber>(shim.LegacyIdToEntity[2]);
        Assert.Equal(0.2f, eb.Magnitude);
        Assert.Equal(5f, eb.OrigY);
    }

    [Fact]
    public void MultipleFamilies_AccumulateOnSameEntity()
    {
        // Dwarf-like archetype: root-level host + Health + Inventory children all fold onto
        // a single Arch entity with all components attached.
        var (ecs, shim) = FreshShim();
        var root = SynthRoot(1);
        var host = SynthHost(2, parent: 1);
        var health = Synth<DwarfCorp.Health>(3, parent: 2); health.Hp = 42;
        var inv = Synth<DwarfCorp.Inventory>(4, parent: 2);
        inv.Resources = new List<DwarfCorp.Inventory.InventoryItem>();
        var sprite = Synth<DwarfCorp.SimpleSprite>(5, parent: 2);

        shim.Migrate(Save(rootId: 1, root, host, health, inv, sprite));

        var entity = shim.LegacyIdToEntity[2];
        Assert.True(ecs.World.Has<EcsHealth>(entity));
        Assert.True(ecs.World.Has<EcsInventory>(entity));
        Assert.True(ecs.World.Has<EcsTinter>(entity));
        Assert.True(ecs.World.Has<EcsSprite>(entity));
    }
}
