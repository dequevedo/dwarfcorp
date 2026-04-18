using System;
using DwarfCorp.Gui;
using Microsoft.Xna.Framework;

namespace DwarfCorp.Gui.Widgets
{
    /// <summary>
    /// Simple centered crosshair drawn on top of the HUD. Used by the Walk-mode camera when
    /// the mouse cursor is hidden so the player has a clear aiming reticle. Visibility is
    /// controlled externally via <see cref="Widget.Hidden"/> and the
    /// <c>GameSettings.Current.ShowCrosshair</c> master toggle.
    /// </summary>
    public class CrosshairOverlay : Widget
    {
        // Length / thickness of each arm (px). Kept small and unobtrusive.
        private const int ArmLength = 12;
        private const int ArmThickness = 2;

        public override void Construct()
        {
            Transparent = true;
            // IMPORTANT: we do NOT use AutoLayout.DockFill here. DockFill consumes the entire
            // remaining layout rect, which would zero out every sibling that comes later in
            // Children (minimap, bottom bar, popups). Instead we anchor Rect manually to the
            // root's full Rect every time the layout runs.
            AutoLayout = AutoLayout.None;
            OnLayout = (sender) =>
            {
                if (Root != null)
                    sender.Rect = Root.RenderData.VirtualScreen;
            };
        }

        protected override Mesh Redraw()
        {
            var mesh = Mesh.EmptyMesh();
            // Honor the master setting — the host toggles Hidden, but if someone forgot,
            // this still won't draw anything when the setting is off.
            if (!GameSettings.Current.ShowCrosshair)
                return mesh;
            // During the very first layout pass Rect can be zero; skip to avoid emitting a
            // degenerate quad to the GL backend.
            if (Rect.Width <= 0 || Rect.Height <= 0 || Root == null)
                return mesh;

            var cx = Rect.X + Rect.Width / 2;
            var cy = Rect.Y + Rect.Height / 2;

            // Use the "basic" white tilesheet (frame 0) and colorize — same pattern used by
            // backgrounds across the codebase, no new asset needed.
            var sheet = Root.GetTileSheet("basic");
            if (sheet == null)
                return mesh;

            var white = new Vector4(1f, 1f, 1f, 0.85f);
            var matrix = sheet.TileMatrix(0);

            // Horizontal arm
            mesh.QuadPart()
                .Scale(ArmLength, ArmThickness)
                .Translate(cx - ArmLength / 2f, cy - ArmThickness / 2f)
                .Texture(matrix)
                .Colorize(white);

            // Vertical arm
            mesh.QuadPart()
                .Scale(ArmThickness, ArmLength)
                .Translate(cx - ArmThickness / 2f, cy - ArmLength / 2f)
                .Texture(matrix)
                .Colorize(white);

            return mesh;
        }
    }
}
