using System;
using System.Collections.Generic;
using System.Linq;
using DwarfCorp.GameStates;
using DwarfCorp.Gui;
using Microsoft.Xna.Framework;

namespace DwarfCorp.Gui.Widgets
{
    /// <summary>
    /// Full-screen transparent widget that draws the name of visible entities projected from
    /// 3D world space to 2D screen space. Toggled by a hotkey / checkbox — inspired by
    /// RimWorld's Alt-key labels.
    ///
    /// Keeps the widget cheap by:
    /// - reusing <see cref="WorldManager.EnumerateIntersectingRootObjectsLoose"/> (already
    ///   frustum-culled via the octree), and
    /// - capping to the 50 closest labelable entities per frame.
    /// </summary>
    public class NameLabelOverlay : Widget
    {
        /// <summary>Host world — required for entity enumeration and camera projection.</summary>
        public WorldManager World;

        /// <summary>External toggle. When false the overlay draws nothing.</summary>
        public Func<bool> IsEnabled = () => false;

        private const int MaxLabels = 50;
        // Entities farther than this many voxels from the camera are skipped entirely.
        private const float MaxDistance = 40f;
        // Squared distance used in filter — cheaper than Sqrt per-entity.
        private static readonly float MaxDistanceSq = MaxDistance * MaxDistance;

        public override void Construct()
        {
            Transparent = true;
            // See CrosshairOverlay for why DockFill is avoided — it would starve every sibling
            // added later in Children, which is how the whole HUD vanished when the minimap's
            // collapse button triggered a full Parent.Layout().
            AutoLayout = AutoLayout.None;
            OnLayout = (sender) =>
            {
                if (Root != null)
                    sender.Rect = Root.RenderData.VirtualScreen;
            };

            OnUpdate = (sender, time) =>
            {
                // Force a redraw every frame while enabled so projected positions stay in sync
                // with the camera. Cheap because the widget's Redraw() early-outs when disabled.
                if (IsEnabled != null && IsEnabled())
                    Invalidate();
            };

            if (Root != null)
                Root.RegisterForUpdate(this);
        }

        protected override Mesh Redraw()
        {
            var mesh = Mesh.EmptyMesh();
            if (IsEnabled == null || !IsEnabled()) return mesh;
            if (World == null || World.Renderer == null || Root == null) return mesh;
            if (Rect.Width <= 0 || Rect.Height <= 0) return mesh;

            var camera = World.Renderer.Camera;
            if (camera == null) return mesh;

            var gameDevice = GameState.Game?.GraphicsDevice;
            if (gameDevice == null) return mesh;

            var viewport = gameDevice.Viewport;
            var frustum = camera.GetDrawFrustum();
            var camPos = camera.Position;

            // Collect candidates: labelable entities intersecting the view frustum.
            var candidates = new List<Tuple<string, Vector3, float>>();
            foreach (var entity in World.EnumerateIntersectingRootObjectsLoose(frustum))
            {
                if (entity == null || !entity.Active) continue;

                var name = GetLabelFor(entity);
                if (string.IsNullOrEmpty(name)) continue;

                var pos = entity.Position;
                var distSq = (pos - camPos).LengthSquared();
                if (distSq > MaxDistanceSq) continue;

                candidates.Add(Tuple.Create(name, pos, distSq));
            }

            // Sort closest first and cap — prevents the overlay from turning into wall-of-text
            // in cluttered scenes.
            candidates.Sort((a, b) => a.Item3.CompareTo(b.Item3));
            if (candidates.Count > MaxLabels)
                candidates.RemoveRange(MaxLabels, candidates.Count - MaxLabels);

            var fontSheet = Root.GetTileSheet("font8");
            if (fontSheet == null)
                fontSheet = Root.GetTileSheet("font10");
            if (fontSheet == null) return mesh;

            var basicSheet = Root.GetTileSheet("basic");
            var basicMatrix = basicSheet?.TileMatrix(0) ?? Matrix.Identity;
            var scale = new Vector2(1f, 1f);

            foreach (var c in candidates)
            {
                var worldPos = c.Item2 + Vector3.Up * 0.7f;
                var projected = viewport.Project(
                    worldPos,
                    camera.ProjectionMatrix,
                    camera.ViewMatrix,
                    Matrix.Identity);

                // Z > ~1 means the point is behind the camera or past the far plane.
                if (projected.Z < 0f || projected.Z >= 0.999f) continue;

                var textSize = Mesh.MeasureStringMesh(c.Item1, fontSheet, scale);
                var labelX = (int)(projected.X - textSize.Width / 2f);
                var labelY = (int)(projected.Y - textSize.Height - 2);

                // Background plate (semi-transparent black) so text stays legible over
                // bright terrain.
                if (basicSheet != null)
                {
                    mesh.QuadPart()
                        .Scale(textSize.Width + 6, textSize.Height + 2)
                        .Translate(labelX - 3, labelY - 1)
                        .Texture(basicMatrix)
                        .Colorize(new Vector4(0f, 0f, 0f, 0.55f));
                }

                // The label text itself.
                Rectangle _b;
                mesh.StringPart(c.Item1, fontSheet, scale, out _b)
                    .Translate(labelX, labelY)
                    .Colorize(new Vector4(1f, 1f, 1f, 1f));
            }

            return mesh;
        }

        /// <summary>
        /// Returns the string to show above <paramref name="entity"/>, or null/empty to skip.
        /// Filters out things we don't want labeled (e.g. particles, generic props).
        /// </summary>
        private static string GetLabelFor(GameComponent entity)
        {
            // Dwarfs / creatures — use their proper name if available.
            if (entity.GetComponent<Creature>().HasValue(out var creature))
            {
                var n = creature.Stats?.FullName;
                return string.IsNullOrEmpty(n) ? entity.Name : n;
            }

            // Resources on the ground (the common RimWorld-style "item name over item").
            if (entity is ResourceEntity) return entity.Name;

            // Anything else with a Resource tag.
            if (entity.Tags != null && entity.Tags.Contains("Resource")) return entity.Name;

            return null;
        }
    }
}
