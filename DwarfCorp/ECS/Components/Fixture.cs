using Microsoft.Xna.Framework;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Fixture"/>. Sprite-sheet
    /// frame + orient mode for decorative/structural placed objects (crates,
    /// torches, statues). Asset is a content-loaded <c>SpriteSheet</c>; kept as
    /// reference during the coexistence phase.
    /// </summary>
    public struct Fixture
    {
        public SpriteSheet Asset;
        public Point Frame;
        public byte OrientMode;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Banner"/> / Flag. Holds the
    /// CompanyInformation pointer (logo, colors, name) — legacy reference type
    /// still; a future Company migration converts it to handle-style lookup.
    /// </summary>
    public struct Banner
    {
        public CompanyInformation Logo;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.MagicalObject"/>. Charges
    /// envelope for chargeable magical items.
    /// </summary>
    public struct MagicalObject
    {
        public int MaxCharges;
        public int CurrentCharges;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.ElevatorShaft"/>. Tracks
    /// linkage to adjacent shaft pieces via legacy <c>GlobalID</c> — future
    /// elevator system migration will replace these with Arch Entity handles.
    /// </summary>
    public struct ElevatorShaft
    {
        public uint TrackAbove;
        public uint TrackBelow;
    }
}
