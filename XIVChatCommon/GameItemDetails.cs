using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace XIVChatCommon {
    [MessagePackObject]
    public sealed class GameDataSource {
        [Key(0)] public string Provider { get; set; } = "game-files";
        [Key(1)] public string Language { get; set; } = "";
        [Key(2)] public string? Version { get; set; }
        [Key(3)] public long RetrievedAtUnixMilliseconds { get; set; }
    }

    [MessagePackObject]
    public sealed class ItemParameter {
        [Key(0)] public uint Id { get; set; }
        [Key(1)] public string Name { get; set; } = "";
        [Key(2)] public int NqValue { get; set; }
        [Key(3)] public int HqDelta { get; set; }
        public int Value(bool hq) => this.NqValue + (hq ? this.HqDelta : 0);
    }

    [MessagePackObject]
    public sealed class GameItemDetails {
        [Key(0)] public List<ItemParameter> Parameters { get; set; } = new();
        [Key(1)] public uint EquipSlotCategoryId { get; set; }
        [Key(2)] public string ClassJobs { get; set; } = "";
        [Key(3)] public bool CanBeHq { get; set; }

        // Join by parameter ID, never by translated labels or array position.
        public static List<ItemParameter> Merge(IEnumerable<ItemParameter> values, IEnumerable<ItemParameter> bonuses) {
            var result = new Dictionary<uint, ItemParameter>();
            foreach (var value in values.Where(p => p.Id > 0))
                result[value.Id] = new ItemParameter { Id = value.Id, Name = value.Name, NqValue = value.NqValue };
            foreach (var bonus in bonuses.Where(p => p.Id > 0)) {
                if (!result.TryGetValue(bonus.Id, out var value))
                    result[bonus.Id] = value = new ItemParameter { Id = bonus.Id, Name = bonus.Name };
                value.HqDelta += bonus.HqDelta;
            }
            return result.Values.ToList();
        }
    }

    public enum GameItemKind : uint { Normal = 0, Collectible = 500_000, Hq = 1_000_000, EventItem = 2_000_000 }

    public static class GameItemIdentity {
        // New senders provide a base ID + kind. Old senders may provide an encoded ID.
        // EventItem IDs belong to a separate sheet and must never be subtracted.
        public static (uint Id, GameItemKind Kind) Resolve(uint id, uint? kind, bool hq) {
            if (kind.HasValue) return (id, (GameItemKind)kind.Value);
            if (id >= 2_000_000) return (id, GameItemKind.EventItem);
            if (id >= 1_000_000) return (id - 1_000_000, GameItemKind.Hq);
            if (id >= 500_000) return (id - 500_000, GameItemKind.Collectible);
            return (id, hq ? GameItemKind.Hq : GameItemKind.Normal);
        }
    }
}
