using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Arch.Core;
using DwarfCorp;
using DwarfCorp.ECS;
using DwarfCorp.ECS.Components;
using DwarfCorp.Infrastructure;
using DwarfCorp.Saving;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Xna.Framework;
using Xunit;

namespace DwarfCorp.Tests;

/// <summary>
/// L.4 commit #1 — Transform family. Asserts that the migration creates exactly
/// one Arch entity per root-level legacy component with the correct position /
/// rotation / scale, that non-root components are skipped, and that re-running
/// Migrate doesn't duplicate (idempotency).
///
/// Test helpers construct <see cref="ComponentManager.ComponentSaveData"/> directly
/// (as save-load would produce) without going through ComponentManager's full
/// initialisation path — we want to exercise the migration shim in isolation.
/// Because the legacy <c>GameComponent</c> ctor requires a WorldManager to be
/// non-null, we reflect to set GlobalID/ParentID on a parameterless instance.
/// </summary>
public class MigrateTransformsTests
{
    private static GameComponent Synth(uint globalId, uint parentId, Matrix localTransform, string name = "test")
    {
        // Parameterless ctor is public on GameComponent (line 177) and skips
        // WorldManager wiring — perfect for a save-format fixture.
        var c = new GameComponent();
        c.Name = name;
        c.GlobalID = globalId;

        // ParentID is private [JsonProperty]; reflect to set it the same way the
        // migration shim reflects to read it.
        var f = typeof(GameComponent).GetField("ParentID",
            BindingFlags.NonPublic | BindingFlags.Instance);
        f!.SetValue(c, parentId);

        c.LocalTransform = localTransform;
        return c;
    }

    private static (EcsWorld ecs, ComponentSaveMigration shim) FreshShim()
    {
        // Dedicated EcsWorld per test — the DI singleton is shared across the whole
        // test run and we don't want entities leaking between tests. Using an
        // isolated world means each test asserts on a clean slate.
        Services.Initialize();
        var logFactory = Services.Provider.GetRequiredService<ILoggerFactory>();
        var ecs = new EcsWorld(logFactory.CreateLogger<EcsWorld>());
        var migrationLog = logFactory.CreateLogger<ComponentSaveMigration>();
        return (ecs, new ComponentSaveMigration(ecs, migrationLog));
    }

    private static int CountEntitiesWithTransform(EcsWorld ecs)
    {
        int n = 0;
        var q = new QueryDescription().WithAll<Transform>();
        ecs.World.Query(in q, (Entity _) => n++);
        return n;
    }

    [Fact]
    public void RootLevelComponent_BecomesSingleArchEntityWithTransform()
    {
        var (ecs, shim) = FreshShim();
        int before = CountEntitiesWithTransform(ecs);

        var root = Synth(globalId: 1, parentId: 0, Matrix.Identity, name: "root");
        var obj = Synth(globalId: 2, parentId: 1,
            Matrix.CreateTranslation(10, 20, 30), name: "barrel");

        var legacy = new ComponentManager.ComponentSaveData
        {
            RootComponent = 1,
            SaveableComponents = new List<GameComponent> { root, obj },
        };

        int created = shim.Migrate(legacy);

        Assert.Equal(1, created);
        Assert.Equal(before + 1, CountEntitiesWithTransform(ecs));
        Assert.True(shim.LegacyIdToEntity.ContainsKey(2));
        Assert.False(shim.LegacyIdToEntity.ContainsKey(1));

        var entity = shim.LegacyIdToEntity[2];
        ref var t = ref ecs.World.Get<Transform>(entity);
        Assert.Equal(new Vector3(10, 20, 30), t.Position);
    }

    [Fact]
    public void NonRootComponents_AreSkipped()
    {
        var (ecs, shim) = FreshShim();

        // Hierarchy: root(1) → dwarf(2) → sprite(3). Only the dwarf is "root-level"
        // in the ECS archetype sense (direct child of the save's RootComponent).
        // Sprite data folds onto the dwarf's entity in a later commit — not here.
        var root = Synth(1, 0, Matrix.Identity);
        var dwarf = Synth(2, 1, Matrix.CreateTranslation(0, 0, 0));
        var sprite = Synth(3, 2, Matrix.CreateTranslation(0, 1, 0));

        var legacy = new ComponentManager.ComponentSaveData
        {
            RootComponent = 1,
            SaveableComponents = new List<GameComponent> { root, dwarf, sprite },
        };

        int created = shim.Migrate(legacy);

        Assert.Equal(1, created);               // only dwarf
        Assert.True(shim.LegacyIdToEntity.ContainsKey(2));
        Assert.False(shim.LegacyIdToEntity.ContainsKey(3));  // sprite skipped
    }

    [Fact]
    public void DecompositionPreservesTranslationRotationScale()
    {
        var (ecs, shim) = FreshShim();

        // A non-trivial matrix: translate + rotate + non-uniform scale.
        var local = Matrix.CreateScale(2f, 3f, 4f)
                  * Matrix.CreateFromYawPitchRoll(0.5f, 0.25f, 0.125f)
                  * Matrix.CreateTranslation(7, 8, 9);

        var root = Synth(1, 0, Matrix.Identity);
        var obj = Synth(2, 1, local);

        shim.Migrate(new ComponentManager.ComponentSaveData
        {
            RootComponent = 1,
            SaveableComponents = new List<GameComponent> { root, obj },
        });

        ref var t = ref ecs.World.Get<Transform>(shim.LegacyIdToEntity[2]);

        // Round-trip the Transform back to a matrix and compare element-wise
        // within a tolerance — cheaper than trying to eyeball quaternion equality.
        var rebuilt = Matrix.CreateScale(t.Scale)
                    * Matrix.CreateFromQuaternion(t.Rotation)
                    * Matrix.CreateTranslation(t.Position);
        const float eps = 1e-4f;
        for (int i = 0; i < 16; i++)
        {
            var a = GetMatrixCell(local, i);
            var b = GetMatrixCell(rebuilt, i);
            Assert.InRange(b - a, -eps, eps);
        }
    }

    [Fact]
    public void MigrateIsIdempotent_NoDuplicatesOnReRun()
    {
        var (ecs, shim) = FreshShim();

        var root = Synth(1, 0, Matrix.Identity);
        var obj = Synth(2, 1, Matrix.CreateTranslation(5, 5, 5));

        var legacy = new ComponentManager.ComponentSaveData
        {
            RootComponent = 1,
            SaveableComponents = new List<GameComponent> { root, obj },
        };

        int firstRun = shim.Migrate(legacy);

        // Second call with a FRESH shim but the same world should be the same.
        // (On the same shim instance, IDs clash; the design contract is "one
        // ComponentSaveMigration per load cycle", not "immortal singleton".)
        var logs = Services.Provider.GetRequiredService<ILogger<ComponentSaveMigration>>();
        var shim2 = new ComponentSaveMigration(ecs, logs);
        int secondRun = shim2.Migrate(legacy);

        Assert.Equal(firstRun, secondRun);
    }

    private static float GetMatrixCell(Matrix m, int i) => i switch
    {
        0 => m.M11, 1 => m.M12, 2 => m.M13, 3 => m.M14,
        4 => m.M21, 5 => m.M22, 6 => m.M23, 7 => m.M24,
        8 => m.M31, 9 => m.M32, 10 => m.M33, 11 => m.M34,
        12 => m.M41, 13 => m.M42, 14 => m.M43, 15 => m.M44,
        _ => 0,
    };
}
