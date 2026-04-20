using System.Collections.Generic;

namespace DwarfCorp.ECS.Components
{
    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Inventory"/>. Holds the list
    /// of items along with their restock/use flags. <c>Resource</c> is a legacy
    /// reference type (not blittable); keeping it as a reference inside the struct
    /// is fine for now — the Arch archetype still stores one pointer per entity and
    /// copies nothing. When the Resource system migrates the list becomes a span of
    /// value-type resource handles.
    /// </summary>
    public struct Inventory
    {
        public List<InventoryItem> Items;
    }

    public struct InventoryItem
    {
        public Resource Resource;
        public bool MarkedForRestock;
        public bool MarkedForUse;
    }

    /// <summary>
    /// Arch snapshot of a legacy <see cref="DwarfCorp.Equipment"/>. Equipped items
    /// are keyed by slot name (the legacy contract); values are <c>Resource</c>
    /// references for the same reasons noted on <see cref="Inventory"/>.
    /// </summary>
    public struct Equipment
    {
        public Dictionary<string, Resource> EquippedItems;
    }
}
