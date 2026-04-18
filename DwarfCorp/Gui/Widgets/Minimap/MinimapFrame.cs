using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DwarfCorp.GameStates;
using DwarfCorp.Gui;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace DwarfCorp.Gui.Widgets.Minimap
{
    public class MinimapFrame : Window
    {
        public String Frame = "minimap-frame";
        public MinimapRenderer Renderer;
        private TextureAtlas.SpriteAtlasEntry DynamicAtlasEntry = null;

        // When Collapsed is true the minimap body is hidden and only the header bar
        // (with zoom and collapse buttons) is drawn. Flip via the −/+ button in the
        // header. Replaces the old close (X) button — the minimap can no longer be
        // fully hidden from here since there's no toolbar icon to re-open it.
        public bool Collapsed = false;
        private Gui.Widgets.ImageButton CollapseButton;
        // Matches buttonRow.MinimumSize.Y below. Small header so the zoom/collapse buttons
        // sit flush against the top border of the frame instead of floating below it.
        private const int HeaderHeight = 16;

        public override Point GetBestSize()
        {
            if (Collapsed)
                return new Point(Renderer.RenderWidth + 20, HeaderHeight + 8);
            return new Point(Renderer.RenderWidth + 20, Renderer.RenderHeight + HeaderHeight + 8);
        }

        public override void Construct()
        {
            MinimumSize = GetBestSize();
            MaximumSize = GetBestSize();

            this.OnUpdate = (sender, time) =>
            {
                if (Hidden)
                    return;

                if (IsAnyParentHidden())
                    return;

                // Skip the expensive render-target update while collapsed — nobody is looking at it.
                if (Collapsed)
                    return;

                Renderer.PreRender(DwarfGame.SpriteBatch);

                if (DynamicAtlasEntry == null)
                {
                    var tex = new Texture2D(Root.RenderData.Device, Renderer.RenderWidth, Renderer.RenderHeight);
                    DynamicAtlasEntry = Root.SpriteAtlas.AddDynamicSheet(null,
                        new TileSheetDefinition
                        {
                            TileWidth = Renderer.RenderWidth,
                            TileHeight = Renderer.RenderHeight,
                            RepeatWhenUsedAsBorder = false,
                            Type = TileSheetType.TileSheet
                        },
                        tex);
                }

                if (Renderer.RenderTarget != null)
                    DynamicAtlasEntry.ReplaceTexture(Renderer.RenderTarget);

                this.Invalidate();
            };

            OnClick = (sender, args) =>
                {
                    if (Collapsed) return;
                    var localX = args.X - Rect.X;
                    var localY = args.Y - Rect.Y;

                    if (localX < Renderer.RenderWidth && localY > HeaderHeight + 2)
                        Renderer.OnClicked(localX, localY);
                };


            var buttonRow = AddChild(new Gui.Widget
            {
                Transparent = true,
                MinimumSize = new Point(0, HeaderHeight),
                AutoLayout = Gui.AutoLayout.DockTop,
                // No vertical padding — the buttons should sit flush at the top border.
                Padding = new Gui.Margin(0, 0, 2, 2)
            });

            buttonRow.AddChild(new Gui.Widgets.ImageButton
                {
                    Background = new Gui.TileReference("round-buttons", 0),
                    MinimumSize = new Point(16, 16),
                    MaximumSize = new Point(16, 16),
                    AutoLayout = Gui.AutoLayout.DockLeft,
                    OnClick = (sender, args) => Renderer.ZoomIn(),
                    Tooltip = "Zoom in"
                });

            buttonRow.AddChild(new Gui.Widgets.ImageButton
            {
                Background = new Gui.TileReference("round-buttons", 1),
                MinimumSize = new Point(16, 16),
                MaximumSize = new Point(16, 16),
                AutoLayout = Gui.AutoLayout.DockLeft,
                OnClick = (sender, args) => Renderer.ZoomOut(),
                Tooltip = "Zoom out"
            });

            buttonRow.AddChild(new Gui.Widgets.ImageButton
            {
                Background = new Gui.TileReference("round-buttons", 2),
                MinimumSize = new Point(16, 16),
                MaximumSize = new Point(16, 16),
                AutoLayout = Gui.AutoLayout.DockLeft,
                OnClick = (sender, args) => Renderer.ZoomHome(),
                Tooltip = "Zoom to home base"
            });

            // Collapse / expand button on the right of the header. Icon 7 = "−" (collapse),
            // icon 3 = "+" (expand) — matches the convention used by CollapsableFrame.
            CollapseButton = buttonRow.AddChild(new Gui.Widgets.ImageButton
            {
                Background = new Gui.TileReference("round-buttons", Collapsed ? 3 : 7),
                MinimumSize = new Point(16, 16),
                MaximumSize = new Point(16, 16),
                AutoLayout = Gui.AutoLayout.DockRight,
                Tooltip = Collapsed ? "Expand minimap" : "Collapse minimap",
                OnClick = (sender, args) => ToggleCollapsed()
            }) as Gui.Widgets.ImageButton;

            OnScroll = (sender, args) =>
            {
                if (Collapsed) return;
                float multiplier = GameSettings.Current.InvertZoom ? 0.001f : -0.001f;
                Renderer.Zoom(args.ScrollValue * multiplier);
            };

            Root.RegisterForUpdate(this);
            base.Construct();
        }

        public void ToggleCollapsed()
        {
            Collapsed = !Collapsed;
            var best = GetBestSize();
            MinimumSize = best;
            MaximumSize = best;
            if (CollapseButton != null)
            {
                CollapseButton.Background = new Gui.TileReference("round-buttons", Collapsed ? 3 : 7);
                CollapseButton.Tooltip = Collapsed ? "Expand minimap" : "Collapse minimap";
                CollapseButton.Invalidate();
            }
            // Re-run layout so the anchored FloatTopRight/FloatBottomLeft position snaps
            // to the new size without leaving a ghost of the old footprint.
            if (Parent != null)
                Parent.Layout();
            Invalidate();
        }



        protected override Gui.Mesh Redraw()
        {
            var mesh = Mesh.EmptyMesh();
            if (!Collapsed && DynamicAtlasEntry != null)
            {
                // Draw the minimap quad below the header (buttons live at the top of the frame).
                var mapY = Rect.Y + HeaderHeight + 2;
                mesh.QuadPart().Scale(Renderer.RenderWidth, Renderer.RenderHeight).Translate(Rect.X + 10, mapY).Texture(DynamicAtlasEntry.TileSheet.TileMatrix(0));
            }
            mesh.Scale9Part(Rect, Root.GetTileSheet("window-transparent"), Scale9Corners.All);
            // Close (X) button intentionally removed — the collapse button in the header
            // is the only way to hide the map body now.
            return mesh;
        }
    }
}
