using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DwarfCorp.Gui;
using System.Linq;

namespace DwarfCorp.Gui.Widgets
{
    public class EmployeePortrait : Widget
    {
        public DwarfSprites.LayerStack Sprite;
        public AnimationPlayer AnimationPlayer;
        private TextureAtlas.SpriteAtlasEntry DynamicAtlasEntry = null;
        // Persistent Texture2D reused every OnUpdate. Previously the code allocated a fresh
        // Texture2D via TextureTool.Texture2DFromMemoryTexture on each tick and handed it to
        // ReplaceTexture — old textures were abandoned to the finalizer (hence the flood of
        // "Texture2D with tag and name was not Disposed" warnings).
        private Texture2D _portraitTexture = null;

        public override void Construct()
        {
            Root.RegisterForUpdate(this);
            base.Construct();

            this.OnUpdate = (sender, time) =>
            {
                if (Hidden || Transparent)
                    return;

                if (IsAnyParentHidden())
                    return;

                if (Sprite == null)
                    return;

                var texture = Sprite.GetCompositeTexture();
                if (texture != null)
                {
                    var sheet = new SpriteSheet(texture, 48, 40);
                    var frame = AnimationPlayer.GetCurrentAnimation().Frames[AnimationPlayer.CurrentFrame];
                    if (DynamicAtlasEntry == null)
                    {
                        _portraitTexture = new Texture2D(Root.RenderData.Device, 48, 40);
                        DynamicAtlasEntry = Root.SpriteAtlas.AddDynamicSheet(null,
                            new TileSheetDefinition
                            {
                                TileHeight = 40,
                                TileWidth = 48,
                                RepeatWhenUsedAsBorder = false,
                                Type = TileSheetType.TileSheet
                            },
                            _portraitTexture);
                    }

                    var memTex = TextureTool.MemoryTextureFromTexture2D(texture, new Rectangle(frame.X * 48, frame.Y * 40, 48, 40));
                    // Reuse the same GPU texture — just rewrite the pixels. ReplaceTexture with
                    // the same instance still flips NeedsBlitToAtlas so the atlas re-blits.
                    TextureTool.CopyMemoryTextureToTexture2D(memTex, _portraitTexture);
                    DynamicAtlasEntry.ReplaceTexture(_portraitTexture);
                }

                this.Invalidate();
            };

            this.OnClose = (sender) =>
            {
                if (DynamicAtlasEntry != null)
                    DynamicAtlasEntry.Discard();
                if (_portraitTexture != null && !_portraitTexture.IsDisposed)
                    _portraitTexture.Dispose();
                _portraitTexture = null;
            };
        }

        protected override Mesh Redraw()
        {
            var r = base.Redraw();
            if (DynamicAtlasEntry != null)
                r.QuadPart()
                    .Scale(Rect.Width, Rect.Height)
                    .Translate(Rect.X, Rect.Y)
                    .Texture(DynamicAtlasEntry.TileSheet.TileMatrix(0));
            return r;
        }
    }
}
