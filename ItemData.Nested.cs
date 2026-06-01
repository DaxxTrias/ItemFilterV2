using System.Collections.Generic;
using System.Linq;
using ExileCore2;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.Shared.Enums;

namespace ItemFilterLibrary;

public partial class ItemData
{
    public sealed class PlayerData
    {
        private static readonly InventorySlotE[] EquippedSlots =
        [
            InventorySlotE.BodyArmour1,
            InventorySlotE.Weapon1,
            InventorySlotE.Offhand1,
            InventorySlotE.Helm1,
            InventorySlotE.Gloves1,
            InventorySlotE.Boots1,
            InventorySlotE.Amulet1,
            InventorySlotE.Ring1,
            InventorySlotE.Ring2,
            InventorySlotE.Ring3,
            InventorySlotE.Belt1,
        ];

        private static readonly InventorySlotE[] OffhandSlots = 
        [
            InventorySlotE.Weapon2,
            InventorySlotE.Offhand2,
        ];

        private readonly List<long> _equippedItemAddresses = [];
        private readonly List<long> _inventoryItemAddresses = [];
        private readonly List<long> _offhandItemAddresses = [];
        public int Level { get; }
        public int Strength { get; }
        public int Dexterity { get; }
        public int Intelligence { get; }

        public List<ItemData> EquippedItems { get; } = [];
        public List<ItemData> OffhandItems { get; } = [];
        public List<ItemData> InventoryItems { get; } = [];
        public List<ItemData> OwnedItems { get; } = [];

        public PlayerData(GameController gameController)
        {
            if (gameController == null)
            {
                return;
            }

            if (gameController.Player.TryGetComponent<Player>(out var playerComp))
            {
                Level = playerComp.Level;
                Strength = playerComp.Strength;
                Dexterity = playerComp.Dexterity;
                Intelligence = playerComp.Intelligence;
            }

            var itemsBySlot = gameController.IngameState.ServerData.PlayerInventories.ToLookup(x => x.Inventory.InventSlot, x => x.Inventory.Items);
            var equippedItems = EquippedSlots.SelectMany(x => itemsBySlot[x].SelectMany(i => i)).Where(IsValidEntityReference).ToList();
            _equippedItemAddresses = equippedItems.Select(x => x.Address).OrderBy(x => x).ToList();
            EquippedItems = CreateSafeItemDataList(equippedItems, gameController);
            var offhandItems = OffhandSlots.SelectMany(x => itemsBySlot[x].SelectMany(i => i)).Where(IsValidEntityReference).ToList();
            _offhandItemAddresses = offhandItems.Select(x => x.Address).OrderBy(x => x).ToList();
            OffhandItems = CreateSafeItemDataList(offhandItems, gameController);
            var inventoryItems = itemsBySlot[InventorySlotE.MainInventory1].SelectMany(x => x).Where(IsValidEntityReference).ToList();
            _inventoryItemAddresses = inventoryItems.Select(x => x.Address).OrderBy(x => x).ToList();
            InventoryItems = CreateSafeItemDataList(inventoryItems, gameController);
            OwnedItems = EquippedItems.Concat(InventoryItems).Concat(OffhandItems).ToList();
        }

        private static bool IsValidEntityReference(Entity? entity)
        {
            try
            {
                return entity != null && entity.Address != 0 && entity.IsValid;
            }
            catch
            {
                return false;
            }
        }

        private static List<ItemData> CreateSafeItemDataList(IEnumerable<Entity> entities, GameController gameController)
        {
            var items = new List<ItemData>();
            foreach (var entity in entities)
            {
                try
                {
                    items.Add(new ItemData(entity, gameController));
                }
                catch
                {
                    // A transient/bad entity should not make every PlayerInfo filter fail.
                }
            }

            return items;
        }

        public bool Equals(PlayerData other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Level == other.Level &&
                   Strength == other.Strength &&
                   Dexterity == other.Dexterity &&
                   Intelligence == other.Intelligence &&
                   _equippedItemAddresses.SequenceEqual(other._equippedItemAddresses) &&
                   _inventoryItemAddresses.SequenceEqual(other._inventoryItemAddresses) &&
                   _offhandItemAddresses.SequenceEqual(other._offhandItemAddresses);
        }
    }

    public sealed class ModsData
    {
        public ModsData(IReadOnlyCollection<ItemMod>? itemMods,
            IReadOnlyCollection<ItemMod>? enchantedMods,
            IReadOnlyCollection<ItemMod>? explicitMods,
            IReadOnlyCollection<ItemMod>? corruptionImplicitMods,
            IReadOnlyCollection<ItemMod>? implicitMods,
            IReadOnlyCollection<ItemMod>? synthesisMods)
        {
            ItemMods = SanitizeMods(itemMods);
            EnchantedMods = SanitizeMods(enchantedMods);
            ExplicitMods = SanitizeMods(explicitMods);
            CorruptionImplicitMods = SanitizeMods(corruptionImplicitMods);
            ImplicitMods = SanitizeMods(implicitMods);
            SynthesisMods = SanitizeMods(synthesisMods);

            ModsDictionary = new Dictionary<IReadOnlyCollection<ItemMod>, string>
            {
                { ItemMods, "ItemMods" },
                { EnchantedMods, "EnchantedMods" },
                { ExplicitMods, "ExplicitMods" },
                { CorruptionImplicitMods, "CorruptionImplicitMods" },
                { ImplicitMods, "ImplicitMods" },
                { SynthesisMods, "SynthesisMods" },
            };

            Prefixes = ExplicitMods.Where(m => m.ModRecord.AffixType == ModType.Prefix).ToList();
            Suffixes = ExplicitMods.Where(m => m.ModRecord.AffixType == ModType.Suffix).ToList();
        }

        public IReadOnlyCollection<ItemMod> ItemMods { get; }
        public IReadOnlyCollection<ItemMod> EnchantedMods { get; }
        public IReadOnlyCollection<ItemMod> ExplicitMods { get; }
        public IReadOnlyCollection<ItemMod> CorruptionImplicitMods { get; }
        public IReadOnlyCollection<ItemMod> ImplicitMods { get; }
        public IReadOnlyCollection<ItemMod> SynthesisMods { get; }
        public IReadOnlyDictionary<IReadOnlyCollection<ItemMod>, string> ModsDictionary { get; }

        public IReadOnlyCollection<ItemMod> Prefixes { get; }
        public IReadOnlyCollection<ItemMod> Suffixes { get; }
        public int MaxAllowedPrefixCount { get; set; } = -1;
        public int MaxAllowedSuffixCount { get; set; } = -1;
        public int OpenPrefixCount { get; set; } = -1;
        public int OpenSuffixCount { get; set; } = -1;
        public bool HasOpenPrefix { get; set; } = false;
        public bool HasOpenSuffix { get; set; } = false;

        private static IReadOnlyCollection<ItemMod> SanitizeMods(IEnumerable<ItemMod>? mods)
        {
            if (mods == null)
                return [];

            try
            {
                return mods.Where(m => m?.ModRecord != null).ToList();
            }
            catch
            {
                return [];
            }
        }
    }
}