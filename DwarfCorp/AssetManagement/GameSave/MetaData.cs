using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace DwarfCorp
{
    public class MetaData
    {
        public float TimeOfDay { get; set; }
        public WorldTime Time { get; set; }
        public String Version;
        public String Commit;
        public WorldRendererPersistentSettings RendererSettings;

        public String DescriptionString = "Description";

        /// <summary>
        /// Save format version. Bumped when the on-disk schema changes in a way that
        /// needs migration code to load older saves. Tracked here so the loader can
        /// branch before it ever sees <see cref="PlayData.Components"/>.
        ///
        /// Current values:
        ///   v1 = legacy ComponentManager + GameComponent JSON tree (TypeNameHandling.Auto).
        ///   v2 = Arch ECS snapshot (Fase L.4 cutover — not yet emitted).
        ///
        /// Defaults to 1 when the field is absent from JSON (older saves), so every
        /// existing .meta on disk reads as v1 without having to be touched.
        /// </summary>
        public int SaveFormatVersion = 1;

        public const int CurrentSaveFormatVersion = 1;

        public static string Extension = "meta";

        public static MetaData CreateFromWorld(WorldManager World)
        {
            return new MetaData
            {
                TimeOfDay = World.Renderer.Sky.TimeOfDay,
                Time = World.Time,
                Version = Program.Version,
                Commit = Program.Commit,
                RendererSettings = World.Renderer.PersistentSettings,
                DescriptionString = String.Format("World size: {0}x{1}\nDwarves: {2}/{3}\nLiquid Assets: {4}\nMaterial Assets: {5}",
                    World.WorldSizeInVoxels.X, World.WorldSizeInVoxels.Z,
                    World.CalculateSupervisedEmployees(), World.CalculateSupervisionCap(),
                    World.PlayerFaction.Economy.Funds.ToString(),
                    World.EnumerateResourcesIncludingMinions().Sum(r => r.MoneyValue).ToString())
            };
        }
    }
}