using Microsoft.Xna.Framework;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch-side vertex-tinting snapshot — the data the legacy
    /// <see cref="DwarfCorp.Tinter"/> base class used to carry on every sprite /
    /// mesh / primitive. Pulled out as its own component because tinting is a
    /// concern shared by several renderers; separating it lets sprite systems
    /// query <c>WithAll&lt;Tinter, Sprite&gt;()</c> or mesh systems query
    /// <c>WithAll&lt;Tinter, InstanceMeshRef&gt;()</c> without cross-talk.
    /// </summary>
    public struct Tinter
    {
        public Color LightRamp;
        public Color VertexColorTint;
        public float TintChangeRate;
        public bool LightsWithVoxels;
        public bool Stipple;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.SimpleSprite"/>. Just the
    /// billboard / orientation metadata; the sprite sheet reference (which is a
    /// content-loaded <c>SpriteSheet</c> instance, not blittable) stays legacy-side
    /// for now. When the rendering system migrates this struct grows a tile-atlas
    /// key or similar content-handle that IS blittable.
    /// </summary>
    public struct Sprite
    {
        /// <summary>Legacy <c>SimpleSprite.OrientMode</c> cast to byte.</summary>
        public byte Orientation;
        public float WorldWidth;
        public float WorldHeight;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.AnimatedSprite"/> or the
    /// <see cref="DwarfCorp.LayeredSimpleSprite"/> / DwarfCharacterSprite variants.
    /// Animation state (current frame, playing, speed) doesn't serialize on the
    /// legacy side (AnimPlayer is <c>[JsonIgnore]</c>), so the struct only carries
    /// the visual-config fields that DO serialize — orientation and silhouette.
    /// </summary>
    public struct AnimatedSprite
    {
        public byte Orientation;
        public Color SilhouetteColor;
        public bool DrawSilhouette;
    }

    /// <summary>
    /// Marker + model-name holder for legacy <see cref="DwarfCorp.InstanceMesh"/>.
    /// The actual instance data (geometry, texture atlas) is reconstructed at runtime
    /// from <see cref="ModelType"/> via the legacy asset pipeline; the ECS side only
    /// needs the key.
    /// </summary>
    public struct InstanceMeshRef
    {
        public string ModelType;
    }
}
