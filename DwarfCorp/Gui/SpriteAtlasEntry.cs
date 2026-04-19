using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DwarfCorp.Gui.TextureAtlas
{
    public class SpriteAtlasEntry
    {
        public TileSheetDefinition SourceDefinition;
        public Texture2D SourceTexture;

        public Rectangle AtlasBounds;
        public ITileSheet TileSheet;
        public bool NeedsBlitToAtlas = false;
        public int ReferenceCount = 0;

        // True when this entry owns SourceTexture — i.e. the texture was allocated by game
        // code specifically to feed the atlas and isn't shared with anything else. The atlas
        // disposes owned textures when the entry's reference count drops to 0 during
        // Prerender(); non-owned textures (content-loaded from AssetManager, render targets
        // shared with renderers, etc.) stay alive and are reclaimed by their original owner.
        public bool OwnsSourceTexture = false;


        public void ReplaceTexture(Texture2D NewTexture)
        {
            // If the entry owned the previous texture and the caller is handing us a new
            // instance, dispose the old one now — otherwise each ReplaceTexture leaks the
            // previous allocation to the finalizer.
            if (OwnsSourceTexture && SourceTexture != null && !ReferenceEquals(SourceTexture, NewTexture) && !SourceTexture.IsDisposed)
                SourceTexture.Dispose();
            SourceTexture = NewTexture;
            NeedsBlitToAtlas = true;
        }

        public void Discard()
        {
            ReferenceCount -= 1;
        }
    }
}
