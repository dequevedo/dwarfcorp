using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;

namespace DwarfCorp.Voxels
{
    public static partial class TemplateSolidLibrary
    {
        private static Dictionary<TemplateSolidShapes, Geo.TemplateSolid> SolidTypes = new Dictionary<TemplateSolidShapes, Geo.TemplateSolid>();
        private static volatile bool TemplateSolidLibraryInitialized = false;
        private static readonly object _initLock = new object();

        private static void InitializeDecalLibrary()
        {
            // Double-checked lock. Previous version set the flag BEFORE populating
            // SolidTypes, which raced under the B.1 parallel chunk-rebuild workers:
            // thread A sets the flag, thread B sees the flag and skips init, both
            // race to read from an empty dict → KeyNotFoundException("HardCube").
            // The race stayed latent until B.2 live put the new geometry path in
            // the default hot loop.
            if (TemplateSolidLibraryInitialized) return;
            lock (_initLock)
            {
                if (TemplateSolidLibraryInitialized) return;

                SolidTypes.Add(TemplateSolidShapes.SoftCube, Geo.TemplateSolid.MakeCube(true, TemplateFaceShapes.SoftSquare));
                SolidTypes.Add(TemplateSolidShapes.HardCube, Geo.TemplateSolid.MakeCube(false, TemplateFaceShapes.Square));
                SolidTypes.Add(TemplateSolidShapes.LowerSlab, Geo.TemplateSolid.MakeLowerSlab());

                Console.WriteLine("Loaded Template Solid Library.");
                TemplateSolidLibraryInitialized = true;
            }
        }

        public static Geo.TemplateSolid GetTemplateSolid(TemplateSolidShapes Shape)
        {
            InitializeDecalLibrary();
            return SolidTypes[Shape];
        }
    }
}