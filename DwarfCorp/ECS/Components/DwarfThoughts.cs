using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.DwarfThoughts"/>. Holds the
    /// memory-of-events list. <c>Thought</c> is a legacy reference type; kept as
    /// ref here during the coexistence phase, becomes a value-type struct when
    /// the Thought model migrates.
    /// </summary>
    public struct DwarfThoughts
    {
        public List<Thought> Thoughts;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Egg"/>. Lifetime + spawn key
    /// for a hatching creature. ParentBody is a GameComponent reference (legacy
    /// ownership link) kept as-is for the snapshot; will be replaced by an Arch
    /// Entity handle once the parent family resolves.
    /// </summary>
    public struct Egg
    {
        public string Adult;
        public DateTime Birthday;
        public GameComponent ParentBody;
        public bool Hatched;
    }
}
