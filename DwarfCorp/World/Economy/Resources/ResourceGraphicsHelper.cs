using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Runtime.Serialization;
using DwarfCorp.Gui;

namespace DwarfCorp
{
    public class ResourceGraphicsHelper
    {
        private static MemoryTexture _GetResourceTexture(GraphicsDevice Device, ResourceType.GuiGraphic Graphic)
        {
            MemoryTexture r = null;
            var rawAsset = AssetManager.GetContentTexture(Graphic.AssetPath);
            if (Graphic.Palette != "None" && DwarfSprites.LayerLibrary.FindPalette(Graphic.Palette).HasValue(out var palette))
                r = TextureTool.CropAndColorSprite(Device, rawAsset, Graphic.FrameSize, Graphic.Frame, DwarfSprites.LayerLibrary.BasePalette.CachedPalette, palette.CachedPalette);
            else
                r = TextureTool.CropSprite(Device, rawAsset, Graphic.FrameSize, Graphic.Frame);

            if (Graphic.NextLayer != null)
                TextureTool.AlphaBlit(_GetResourceTexture(Device, Graphic.NextLayer), new Rectangle(0, 0, Graphic.NextLayer.FrameSize.X, Graphic.NextLayer.FrameSize.Y),
                    r, new Point(0, 0));
            return r;
        }

        // Process-wide cache of resource textures, keyed by Graphic.GetSheetIdentifier().
        // Same texture is shared by the GUI (via GetDynamicSheet) and world entities (via
        // ResourceEntity). Previously every caller allocated a fresh Texture2D via
        // TextureTool.Texture2DFromMemoryTexture — thousands of orphaned textures per play
        // session, cascading into heap corruption on AMD Vulkan. Now one texture per
        // resource type for the lifetime of the process.
        private static readonly ConcurrentDictionary<string, Texture2D> _sharedTextureCache =
            new ConcurrentDictionary<string, Texture2D>();

        /// <summary>
        /// Returns a process-lifetime shared Texture2D for the given Graphic. Never dispose
        /// the returned texture — it's owned by the cache and may be referenced from
        /// multiple call sites simultaneously.
        /// </summary>
        public static Texture2D GetOrCreateSharedTexture(GraphicsDevice Device, ResourceType.GuiGraphic Graphic)
        {
            var key = Graphic.GetSheetIdentifier();
            if (_sharedTextureCache.TryGetValue(key, out var cached) && cached != null && !cached.IsDisposed)
                return cached;

            // `new Texture2D` and SetData are GPU calls — they must serialize against the
            // render thread on FNA 26 Vulkan. Called from ResourceEntity.CreateCosmeticChildren
            // which can run on worker / AI / deserialization threads.
            Texture2D freshly;
            lock (DwarfGame.GpuLock)
            {
                freshly = TextureTool.Texture2DFromMemoryTexture(Device, _GetResourceTexture(Device, Graphic));
            }
            // Last-writer wins on concurrent create — the loser's texture is disposed to avoid
            // a leak. Unlikely in practice since we're already serialized by GpuLock, but cheap.
            var stored = _sharedTextureCache.GetOrAdd(key, freshly);
            if (!ReferenceEquals(stored, freshly) && !freshly.IsDisposed)
            {
                lock (DwarfGame.GpuLock)
                    freshly.Dispose();
            }
            return stored;
        }

        // Kept for external callers that still want a per-call Texture2D (i.e. they take
        // ownership themselves). Internal DwarfCorp call sites should prefer
        // GetOrCreateSharedTexture to avoid per-call allocation.
        public static Texture2D GetResourceTexture(GraphicsDevice Device, ResourceType.GuiGraphic Graphic)
        {
            return TextureTool.Texture2DFromMemoryTexture(Device, _GetResourceTexture(Device, Graphic));
        }

        public static Gui.TextureAtlas.SpriteAtlasEntry GetDynamicSheet(Gui.Root Root, Resource Resource)
        {
            return GetDynamicSheet(Root, Resource.Gui_Graphic);
        }

        public static Gui.TextureAtlas.SpriteAtlasEntry GetDynamicSheet(Gui.Root Root, ResourceType.GuiGraphic Graphic)
        {
            if (Graphic == null)
            {
                // "error" sheet uses a content-loaded texture — shared, never owned by the
                // atlas. ownsTexture defaults to false.
                var tex = AssetManager.GetContentTexture("newgui/error");
                return Root.SpriteAtlas.AddDynamicSheet("error", new TileSheetDefinition
                {
                    TileHeight = 32,
                    TileWidth = 32,
                    Type = TileSheetType.TileSheet
                }, tex);
            }
            else
            {
                var sheetName = Graphic.GetSheetIdentifier();

                // Check the atlas cache BEFORE allocating anything. Previously this call
                // always allocated a Texture2D via GetResourceTexture and passed it to
                // AddDynamicSheet — if the name was already cached the texture was thrown
                // on the floor and leaked.
                var cached = Root.SpriteAtlas.TryGetCachedDynamicSheet(sheetName);
                if (cached != null) return cached;

                // Use the shared-texture cache: one Texture2D per resource type, reused by
                // every caller for the process lifetime. Atlas does NOT own this texture
                // (it's shared with ResourceEntity etc.), so ownsTexture=false.
                var tex = GetOrCreateSharedTexture(Root.RenderData.Device, Graphic);
                return Root.SpriteAtlas.AddDynamicSheet(sheetName, new TileSheetDefinition
                {
                    TileHeight = Graphic.FrameSize.Y,
                    TileWidth = Graphic.FrameSize.X,
                    Type = TileSheetType.TileSheet
                }, tex);
            }
        }
    }
}
