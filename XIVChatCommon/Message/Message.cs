using System;
using System.Collections.Generic;
using MessagePack;
using MessagePack.Formatters;

namespace XIVChatCommon.Message {
    [Union(1, typeof(TextChunk))]
    [Union(2, typeof(IconChunk))]
    [MessagePackObject]
    public abstract class Chunk {
    }

    [MessagePackObject]
    public class TextChunk : Chunk {
        [Key(0)]
        public uint? FallbackColour { get; set; }

        [Key(1)]
        public uint? Foreground { get; set; }

        [Key(2)]
        public uint? Glow { get; set; }

        [Key(3)]
        public bool Italic { get; set; }

        [Key(4)]
        public string Content { get; set; }

        [Key(5)]
        public uint? MapId { get; set; }

        [Key(6)]
        public float? MapX { get; set; }

        [Key(7)]
        public float? MapY { get; set; }

        [Key(8)]
        public uint? ItemId { get; set; }

        [Key(9)]
        public bool? IsHq { get; set; }

        [Key(10)]
        public string? ItemName { get; set; }

        [Key(11)]
        public string? ItemDescription { get; set; }

        [Key(12)]
        public uint? ItemIconId { get; set; }

        [Key(13)]
        public ushort? ItemLevel { get; set; }

        [Key(14)]
        public byte? ItemRarity { get; set; }

        [Key(15)]
        public string? ItemCategory { get; set; }

        [Key(16)]
        public ushort? ItemEquipLevel { get; set; }

        [Key(17)]
        public byte? ItemMateriaSlots { get; set; }

        [Key(18)]
        public bool? ItemIsAdvancedMeldingPermitted { get; set; }

        [Key(19)]
        public List<string>? ItemStats { get; set; }

        [Key(20)]
        public string? MapPlaceName { get; set; }

        [Key(21)]
        public string? MapFilenameId { get; set; }

        [Key(22)]
        public ushort? MapSizeFactor { get; set; }

        public TextChunk(string content) {
            this.Content = content;
        }

        public TextChunk(uint? fallbackColour, uint? foreground, uint? glow, bool italic, string content, uint? mapId = null, float? mapX = null, float? mapY = null, uint? itemId = null, bool? isHq = null) {
            this.FallbackColour = fallbackColour;
            this.Foreground = foreground;
            this.Glow = glow;
            this.Italic = italic;
            this.Content = content;
            this.MapId = mapId;
            this.MapX = mapX;
            this.MapY = mapY;
            this.ItemId = itemId;
            this.IsHq = isHq;
        }
    }

    [MessagePackObject]
    public class IconChunk : Chunk {
        [Key(0)]
        public byte index;
    }

    public class ChatCode {
        private const ushort Clear7 = ~(~0 << 7);

        public ushort Raw { get; }

        public ChatType Type => (ChatType) (this.Raw & Clear7);
        public ChatSource Source => this.SourceFrom(11);
        public ChatSource Target => this.SourceFrom(7);
        private ChatSource SourceFrom(ushort shift) => (ChatSource) (1 << ((this.Raw >> shift) & 0xF));

        public ChatCode(ushort raw) {
            this.Raw = raw;
        }

        public ChatType Parent() => this.Type switch {
            ChatType.Say => ChatType.Say,
            ChatType.GmSay => ChatType.Say,
            ChatType.Shout => ChatType.Shout,
            ChatType.GmShout => ChatType.Shout,
            ChatType.TellOutgoing => ChatType.TellOutgoing,
            ChatType.TellIncoming => ChatType.TellOutgoing,
            ChatType.GmTell => ChatType.TellOutgoing,
            ChatType.Party => ChatType.Party,
            ChatType.CrossParty => ChatType.Party,
            ChatType.GmParty => ChatType.Party,
            ChatType.Linkshell1 => ChatType.Linkshell1,
            ChatType.GmLinkshell1 => ChatType.Linkshell1,
            ChatType.Linkshell2 => ChatType.Linkshell2,
            ChatType.GmLinkshell2 => ChatType.Linkshell2,
            ChatType.Linkshell3 => ChatType.Linkshell3,
            ChatType.GmLinkshell3 => ChatType.Linkshell3,
            ChatType.Linkshell4 => ChatType.Linkshell4,
            ChatType.GmLinkshell4 => ChatType.Linkshell4,
            ChatType.Linkshell5 => ChatType.Linkshell5,
            ChatType.GmLinkshell5 => ChatType.Linkshell5,
            ChatType.Linkshell6 => ChatType.Linkshell6,
            ChatType.GmLinkshell6 => ChatType.Linkshell6,
            ChatType.Linkshell7 => ChatType.Linkshell7,
            ChatType.GmLinkshell7 => ChatType.Linkshell7,
            ChatType.Linkshell8 => ChatType.Linkshell8,
            ChatType.GmLinkshell8 => ChatType.Linkshell8,
            ChatType.FreeCompany => ChatType.FreeCompany,
            ChatType.GmFreeCompany => ChatType.FreeCompany,
            ChatType.NoviceNetwork => ChatType.NoviceNetwork,
            ChatType.GmNoviceNetwork => ChatType.NoviceNetwork,
            ChatType.CustomEmote => ChatType.CustomEmote,
            ChatType.StandardEmote => ChatType.StandardEmote,
            ChatType.Yell => ChatType.Yell,
            ChatType.GmYell => ChatType.Yell,
            ChatType.GainBuff => ChatType.GainBuff,
            ChatType.LoseBuff => ChatType.GainBuff,
            ChatType.GainDebuff => ChatType.GainDebuff,
            ChatType.LoseDebuff => ChatType.GainDebuff,
            ChatType.System => ChatType.System,
            ChatType.Alarm => ChatType.System,
            ChatType.RetainerSale => ChatType.System,
            ChatType.PeriodicRecruitmentNotification => ChatType.System,
            ChatType.Sign => ChatType.System,
            ChatType.Orchestrion => ChatType.System,
            ChatType.MessageBook => ChatType.System,
            ChatType.NpcDialogue => ChatType.NpcDialogue,
            ChatType.NpcAnnouncement => ChatType.NpcDialogue,
            ChatType.LootRoll => ChatType.LootRoll,
            ChatType.RandomNumber => ChatType.LootRoll,
            ChatType.FreeCompanyAnnouncement => ChatType.FreeCompanyAnnouncement,
            ChatType.FreeCompanyLoginLogout => ChatType.FreeCompanyAnnouncement,
            ChatType.PvpTeamAnnouncement => ChatType.PvpTeamAnnouncement,
            ChatType.PvpTeamLoginLogout => ChatType.PvpTeamAnnouncement,
            _ => this.Type,
        };

        //public string ConfigKey() {
        //    switch (this.Type) {
        //        case ChatType.Say:
        //        case ChatType.GmSay:
        //            return "ColorSay";
        //        case ChatType.Shout:
        //        case ChatType.GmShout:
        //            return "ColorShout";
        //        case ChatType.TellOutgoing:
        //        case ChatType.TellIncoming:
        //        case ChatType.GmTell:
        //            return "ColorTell";
        //        case ChatType.Party:
        //        case ChatType.CrossParty:
        //        case ChatType.GmParty:
        //            return "ColorParty";
        //        case ChatType.Alliance:
        //            return "ColorAlliance";
        //        case ChatType.Linkshell1:
        //        case ChatType.GmLinkshell1:
        //            return "ColorLS1";
        //        case ChatType.Linkshell2:
        //        case ChatType.GmLinkshell2:
        //            return "ColorLS2";
        //        case ChatType.Linkshell3:
        //        case ChatType.GmLinkshell3:
        //            return "ColorLS3";
        //        case ChatType.Linkshell4:
        //        case ChatType.GmLinkshell4:
        //            return "ColorLS4";
        //        case ChatType.Linkshell5:
        //        case ChatType.GmLinkshell5:
        //            return "ColorLS5";
        //        case ChatType.Linkshell6:
        //        case ChatType.GmLinkshell6:
        //            return "ColorLS6";
        //        case ChatType.Linkshell7:
        //        case ChatType.GmLinkshell7:
        //            return "ColorLS7";
        //        case ChatType.Linkshell8:
        //        case ChatType.GmLinkshell8:
        //            return "ColorLS8";
        //        case ChatType.FreeCompany:
        //        case ChatType.GmFreeCompany:
        //            return "ColorFCompany";
        //        case ChatType.NoviceNetwork:
        //        case ChatType.GmNoviceNetwork:
        //            return "ColorBeginner";
        //        case ChatType.CustomEmote:
        //            return "ColorEmoteUser";
        //        case ChatType.StandardEmote:
        //            return "ColorEmote";
        //        case ChatType.Yell:
        //        case ChatType.GmYell:
        //            return "ColorYell";
        //        case ChatType.PvpTeam:
        //            return "ColorPvPGroup";
        //        case ChatType.CrossLinkshell1:
        //            return "ColorCWLS";
        //        case ChatType.Damage:
        //            return "ColorAttackSuccess";
        //        case ChatType.Miss:
        //            return "ColorAttackFailure";
        //        case ChatType.Action:
        //            return "ColorAction";
        //        case ChatType.Item:
        //            return "ColorItem";
        //        case ChatType.Healing:
        //            return "ColorCureGive";
        //        case ChatType.GainBuff:
        //        case ChatType.GainDebuff:
        //            return "ColorBuffGive";
        //        case ChatType.LoseBuff:
        //        case ChatType.LoseDebuff:
        //            return "ColorDebuffGive";
        //        case ChatType.Echo:
        //            return "ColorEcho";
        //        case ChatType.System:
        //        case ChatType.Alarm:
        //        case ChatType.RetainerSale:
        //        case ChatType.PeriodicRecruitmentNotification:
        //        case ChatType.Sign:
        //        case ChatType.Orchestrion:
        //        case ChatType.MessageBook:
        //            return "ColorSysMsg";
        //        case ChatType.BattleSystem:
        //            return "ColorSysBattle";
        //        case ChatType.GatheringSystem:
        //            return "ColorSysGathering";
        //        case ChatType.Error:
        //            return "ColorSysError";
        //        case ChatType.NpcDialogue:
        //        case ChatType.NpcAnnouncement:
        //            return "ColorNpcSay";
        //        case ChatType.LootNotice:
        //            return "ColorItemNotice";
        //        case ChatType.Progress:
        //            return "ColorGrowup";
        //        case ChatType.LootRoll:
        //        case ChatType.RandomNumber:
        //            return "ColorLoot";
        //        case ChatType.Crafting:
        //            return "ColorCraft";
        //        case ChatType.Gathering:
        //            return "ColorGathering";
        //        case ChatType.FreeCompanyAnnouncement:
        //        case ChatType.FreeCompanyLoginLogout:
        //            return "ColorFCAnnounce";
        //        case ChatType.NoviceNetworkSystem:
        //            return "ColorBeginnerAnnounce";
        //        case ChatType.PvpTeamAnnouncement:
        //        case ChatType.PvpTeamLoginLogout:
        //            return "ColorPvPGroupAnnounce";
        //        case ChatType.CrossLinkshell2:
        //            return "ColorCWLS2";
        //        case ChatType.CrossLinkshell3:
        //            return "ColorCWLS3";
        //        case ChatType.CrossLinkshell4:
        //            return "ColorCWLS4";
        //        case ChatType.CrossLinkshell5:
        //            return "ColorCWLS5";
        //        case ChatType.CrossLinkshell6:
        //            return "ColorCWLS6";
        //        case ChatType.CrossLinkshell7:
        //            return "ColorCWLS7";
        //        case ChatType.CrossLinkshell8:
        //            return "ColorCWLS8";
        //        default:
        //            return null;
        //    }
        //}

        public bool IsBattle() {
            switch (this.Type) {
                case ChatType.Damage:
                case ChatType.Miss:
                case ChatType.Action:
                case ChatType.Item:
                case ChatType.Healing:
                case ChatType.GainBuff:
                case ChatType.LoseBuff:
                case ChatType.GainDebuff:
                case ChatType.LoseDebuff:
                case ChatType.BattleSystem:
                    return true;
                default:
                    return false;
            }
        }

        public uint? DefaultColour() {
            switch (this.Type) {
                case ChatType.Debug:
                    return Rgba(204, 204, 204);
                case ChatType.Urgent:
                    return Rgba(255, 127, 127);
                case ChatType.Notice:
                    return Rgba(179, 140, 255);

                case ChatType.Say:
                    return Rgba(247, 247, 247);
                case ChatType.Shout:
                    return Rgba(255, 166, 102);
                case ChatType.TellIncoming:
                case ChatType.TellOutgoing:
                case ChatType.GmTell:
                    return Rgba(255, 184, 222);
                case ChatType.Party:
                case ChatType.CrossParty:
                    return Rgba(102, 229, 255);
                case ChatType.Alliance:
                    return Rgba(255, 127, 0);
                case ChatType.NoviceNetwork:
                case ChatType.NoviceNetworkSystem:
                    return Rgba(212, 255, 125);
                case ChatType.Linkshell1:
                case ChatType.Linkshell2:
                case ChatType.Linkshell3:
                case ChatType.Linkshell4:
                case ChatType.Linkshell5:
                case ChatType.Linkshell6:
                case ChatType.Linkshell7:
                case ChatType.Linkshell8:
                case ChatType.CrossLinkshell1:
                case ChatType.CrossLinkshell2:
                case ChatType.CrossLinkshell3:
                case ChatType.CrossLinkshell4:
                case ChatType.CrossLinkshell5:
                case ChatType.CrossLinkshell6:
                case ChatType.CrossLinkshell7:
                case ChatType.CrossLinkshell8:
                    return Rgba(212, 255, 125);
                case ChatType.StandardEmote:
                    return Rgba(186, 255, 240);
                case ChatType.CustomEmote:
                    return Rgba(186, 255, 240);
                case ChatType.Yell:
                    return Rgba(255, 255, 0);
                case ChatType.Echo:
                    return Rgba(204, 204, 204);
                case ChatType.System:
                case ChatType.GatheringSystem:
                case ChatType.PeriodicRecruitmentNotification:
                case ChatType.Orchestrion:
                case ChatType.Alarm:
                case ChatType.RetainerSale:
                case ChatType.Sign:
                case ChatType.MessageBook:
                    return Rgba(204, 204, 204);
                case ChatType.NpcAnnouncement:
                case ChatType.NpcDialogue:
                    return Rgba(171, 214, 71);
                case ChatType.Error:
                    return Rgba(255, 74, 74);
                case ChatType.FreeCompany:
                case ChatType.FreeCompanyAnnouncement:
                case ChatType.FreeCompanyLoginLogout:
                    return Rgba(171, 219, 229);
                case ChatType.PvpTeam:
                    return Rgba(171, 219, 229);
                case ChatType.PvpTeamAnnouncement:
                case ChatType.PvpTeamLoginLogout:
                    return Rgba(171, 219, 229);
                case ChatType.Action:
                case ChatType.Item:
                case ChatType.LootNotice:
                    return Rgba(255, 255, 176);
                case ChatType.Progress:
                    return Rgba(255, 222, 115);
                case ChatType.LootRoll:
                case ChatType.RandomNumber:
                    return Rgba(199, 191, 158);
                case ChatType.Crafting:
                case ChatType.Gathering:
                    return Rgba(222, 191, 247);
                case ChatType.Damage:
                    return Rgba(255, 125, 125);
                case ChatType.Miss:
                    return Rgba(204, 204, 204);
                case ChatType.Healing:
                    return Rgba(212, 255, 125);
                case ChatType.GainBuff:
                case ChatType.LoseBuff:
                    return Rgba(148, 191, 255);
                case ChatType.GainDebuff:
                case ChatType.LoseDebuff:
                    return Rgba(255, 138, 196);
                case ChatType.BattleSystem:
                    return Rgba(204, 204, 204);
                default:
                    return null;
            }
        }

        private static uint Rgba(byte red, byte green, byte blue, byte alpha = 0xFF) => alpha
                                                                                        | (uint) (red << 24)
                                                                                        | (uint) (green << 16)
                                                                                        | (uint) (blue << 8);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32")]
    public enum ChatType : ushort {
        Debug = 1,
        Urgent = 2,
        Notice = 3,
        Say = 10,
        Shout = 11,
        TellOutgoing = 12,
        TellIncoming = 13,
        Party = 14,
        Alliance = 15,
        Linkshell1 = 16,
        Linkshell2 = 17,
        Linkshell3 = 18,
        Linkshell4 = 19,
        Linkshell5 = 20,
        Linkshell6 = 21,
        Linkshell7 = 22,
        Linkshell8 = 23,
        FreeCompany = 24,
        NoviceNetwork = 27,
        CustomEmote = 28,
        StandardEmote = 29,
        Yell = 30,

        // 31 - also party?
        CrossParty = 32,
        PvpTeam = 36,
        CrossLinkshell1 = 37,
        Damage = 41,
        Miss = 42,
        Action = 43,
        Item = 44,
        Healing = 45,
        GainBuff = 46,
        GainDebuff = 47,
        LoseBuff = 48,
        LoseDebuff = 49,
        Alarm = 55,
        Echo = 56,
        System = 57,
        BattleSystem = 58,
        GatheringSystem = 59,
        Error = 60,
        NpcDialogue = 61,
        LootNotice = 62,
        Progress = 64,
        LootRoll = 65,
        Crafting = 66,
        Gathering = 67,
        NpcAnnouncement = 68,
        FreeCompanyAnnouncement = 69,
        FreeCompanyLoginLogout = 70,
        RetainerSale = 71,
        PeriodicRecruitmentNotification = 72,
        Sign = 73,
        RandomNumber = 74,
        NoviceNetworkSystem = 75,
        Orchestrion = 76,
        PvpTeamAnnouncement = 77,
        PvpTeamLoginLogout = 78,
        MessageBook = 79,
        GmTell = 80,
        GmSay = 81,
        GmShout = 82,
        GmYell = 83,
        GmParty = 84,
        GmFreeCompany = 85,
        GmLinkshell1 = 86,
        GmLinkshell2 = 87,
        GmLinkshell3 = 88,
        GmLinkshell4 = 89,
        GmLinkshell5 = 90,
        GmLinkshell6 = 91,
        GmLinkshell7 = 92,
        GmLinkshell8 = 93,
        GmNoviceNetwork = 94,
        CrossLinkshell2 = 101,
        CrossLinkshell3 = 102,
        CrossLinkshell4 = 103,
        CrossLinkshell5 = 104,
        CrossLinkshell6 = 105,
        CrossLinkshell7 = 106,
        CrossLinkshell8 = 107,
    }

    public static class ChatTypeExt {
        public static string? Name(this ChatType type) {
            return type switch {
                ChatType.Debug => "Debug",
                ChatType.Urgent => "Urgent",
                ChatType.Notice => "Notice",
                ChatType.Say => "Say",
                ChatType.Shout => "Shout",
                ChatType.TellOutgoing => "Tell (Outgoing)",
                ChatType.TellIncoming => "Tell (Incoming)",
                ChatType.Party => "Party",
                ChatType.Alliance => "Alliance",
                ChatType.Linkshell1 => "Linkshell [1]",
                ChatType.Linkshell2 => "Linkshell [2]",
                ChatType.Linkshell3 => "Linkshell [3]",
                ChatType.Linkshell4 => "Linkshell [4]",
                ChatType.Linkshell5 => "Linkshell [5]",
                ChatType.Linkshell6 => "Linkshell [6]",
                ChatType.Linkshell7 => "Linkshell [7]",
                ChatType.Linkshell8 => "Linkshell [8]",
                ChatType.FreeCompany => "Free Company",
                ChatType.NoviceNetwork => "Novice Network",
                ChatType.CustomEmote => "Custom Emotes",
                ChatType.StandardEmote => "Standard Emotes",
                ChatType.Yell => "Yell",
                ChatType.CrossParty => "Cross-world Party",
                ChatType.PvpTeam => "PvP Team",
                ChatType.CrossLinkshell1 => "Cross-world Linkshell [1]",
                ChatType.Damage => "Damage dealt",
                ChatType.Miss => "Failed attacks",
                ChatType.Action => "Actions used",
                ChatType.Item => "Items used",
                ChatType.Healing => "Healing",
                ChatType.GainBuff => "Beneficial effects granted",
                ChatType.GainDebuff => "Detrimental effects inflicted",
                ChatType.LoseBuff => "Beneficial effects lost",
                ChatType.LoseDebuff => "Detrimental effects cured",
                ChatType.Alarm => "Alarm Notifications",
                ChatType.Echo => "Echo",
                ChatType.System => "System Messages",
                ChatType.BattleSystem => "Battle System Messages",
                ChatType.GatheringSystem => "Gathering System Messages",
                ChatType.Error => "Error Messages",
                ChatType.NpcDialogue => "NPC Dialogue",
                ChatType.LootNotice => "Loot Notices",
                ChatType.Progress => "Progression Messages",
                ChatType.LootRoll => "Loot Messages",
                ChatType.Crafting => "Synthesis Messages",
                ChatType.Gathering => "Gathering Messages",
                ChatType.NpcAnnouncement => "NPC Dialogue (Announcements)",
                ChatType.FreeCompanyAnnouncement => "Free Company Announcements",
                ChatType.FreeCompanyLoginLogout => "Free Company Member Login Notifications",
                ChatType.RetainerSale => "Retainer Sale Notifications",
                ChatType.PeriodicRecruitmentNotification => "Periodic Recruitment Notifications",
                ChatType.Sign => "Sign Messages for PC Targets",
                ChatType.RandomNumber => "Random Number Messages",
                ChatType.NoviceNetworkSystem => "Novice Network Notifications",
                ChatType.Orchestrion => "Current Orchestrion Track Messages",
                ChatType.PvpTeamAnnouncement => "PvP Team Announcements",
                ChatType.PvpTeamLoginLogout => "PvP Team Member Login Notifications",
                ChatType.MessageBook => "Message Book Alert",
                ChatType.GmTell => "Tell (GM)",
                ChatType.GmSay => "Say (GM)",
                ChatType.GmShout => "Shout (GM)",
                ChatType.GmYell => "Yell (GM)",
                ChatType.GmParty => "Party (GM)",
                ChatType.GmFreeCompany => "Free Company (GM)",
                ChatType.GmLinkshell1 => "Linkshell [1] (GM)",
                ChatType.GmLinkshell2 => "Linkshell [2] (GM)",
                ChatType.GmLinkshell3 => "Linkshell [3] (GM)",
                ChatType.GmLinkshell4 => "Linkshell [4] (GM)",
                ChatType.GmLinkshell5 => "Linkshell [5] (GM)",
                ChatType.GmLinkshell6 => "Linkshell [6] (GM)",
                ChatType.GmLinkshell7 => "Linkshell [7] (GM)",
                ChatType.GmLinkshell8 => "Linkshell [8] (GM)",
                ChatType.GmNoviceNetwork => "Novice Network (GM)",
                ChatType.CrossLinkshell2 => "Cross-world Linkshell [2]",
                ChatType.CrossLinkshell3 => "Cross-world Linkshell [3]",
                ChatType.CrossLinkshell4 => "Cross-world Linkshell [4]",
                ChatType.CrossLinkshell5 => "Cross-world Linkshell [5]",
                ChatType.CrossLinkshell6 => "Cross-world Linkshell [6]",
                ChatType.CrossLinkshell7 => "Cross-world Linkshell [7]",
                ChatType.CrossLinkshell8 => "Cross-world Linkshell [8]",
                _ => type.ToString(),
            };
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1028:Enum Storage should be Int32")]
    public enum ChatSource : ushort {
        Self = 2,
        PartyMember = 4,
        AllianceMember = 8,
        Other = 16,
        EngagedEnemy = 32,
        UnengagedEnemy = 64,
        FriendlyNpc = 128,
        SelfPet = 256,
        PartyPet = 512,
        AlliancePet = 1024,
        OtherPet = 2048,
    }

    public enum InputChannel : uint {
        Tell = 0,
        Say = 1,
        Party = 2,
        Alliance = 3,
        Yell = 4,
        Shout = 5,
        FreeCompany = 6,
        PvpTeam = 7,
        NoviceNetwork = 8,
        CrossLinkshell1 = 9,
        CrossLinkshell2 = 10,
        CrossLinkshell3 = 11,
        CrossLinkshell4 = 12,
        CrossLinkshell5 = 13,
        CrossLinkshell6 = 14,
        CrossLinkshell7 = 15,
        CrossLinkshell8 = 16,

        // 17 - unused?
        // 18 - unused?
        Linkshell1 = 19,
        Linkshell2 = 20,
        Linkshell3 = 21,
        Linkshell4 = 22,
        Linkshell5 = 23,
        Linkshell6 = 24,
        Linkshell7 = 25,
        Linkshell8 = 26,
    }

    public static class InputChannelExt {
        public static uint LinkshellIndex(this InputChannel channel) => channel switch {
            InputChannel.Linkshell1 => 0,
            InputChannel.Linkshell2 => 1,
            InputChannel.Linkshell3 => 2,
            InputChannel.Linkshell4 => 3,
            InputChannel.Linkshell5 => 4,
            InputChannel.Linkshell6 => 5,
            InputChannel.Linkshell7 => 6,
            InputChannel.Linkshell8 => 7,
            InputChannel.CrossLinkshell1 => 0,
            InputChannel.CrossLinkshell2 => 1,
            InputChannel.CrossLinkshell3 => 2,
            InputChannel.CrossLinkshell4 => 3,
            InputChannel.CrossLinkshell5 => 4,
            InputChannel.CrossLinkshell6 => 5,
            InputChannel.CrossLinkshell7 => 6,
            InputChannel.CrossLinkshell8 => 7,
            _ => 0,
        };
    }

    public enum PlayerListType : byte {
        Party = 1,
        Friend = 2,
        Linkshell = 3,
        CrossLinkshell = 4,
        Targeting = 5,
    }

    [MessagePackObject]
    public class Player {
        [Key(0)]
        public string? Name { get; set; }

        [Key(1)]
        public string? FreeCompany { get; set; }

        [Key(2)]
        public ulong Status { get; set; }

        [Key(3)]
        public ushort CurrentWorld { get; set; }

        [Key(4)]
        public string? CurrentWorldName { get; set; }

        [Key(5)]
        public ushort HomeWorld { get; set; }

        [Key(6)]
        public string? HomeWorldName { get; set; }

        [Key(7)]
        public ushort Territory { get; set; }

        [Key(8)]
        public string? TerritoryName { get; set; }

        [Key(9)]
        public byte Job { get; set; }

        [Key(10)]
        public string? JobName { get; set; }

        [Key(11)]
        public byte GrandCompany { get; set; }

        [Key(12)]
        public string? GrandCompanyName { get; set; }

        [Key(13)]
        public byte Languages { get; set; }

        [Key(14)]
        public byte MainLanguage { get; set; }

        public bool HasStatus(PlayerStatus status) => (this.Status & ((ulong) 1 << (int) status)) > 0;
    }

    public enum PlayerStatus {
        GameQa = 1,
        GameMaster1 = 2,
        GameMaster2 = 3,
        EventParticipant = 4,
        Disconnected = 5,
        WaitingForFriendListApproval = 6,
        WaitingForLinkshellApproval = 7,
        WaitingForFreeCompanyApproval = 8,
        NotFound = 9,
        Offline = 10,
        BattleMentor = 11,
        Busy = 12,
        Pvp = 13,
        PlayingTripleTriad = 14,
        ViewingCutscene = 15,
        UsingAChocoboPorter = 16,
        AwayFromKeyboard = 17,
        CameraMode = 18,
        LookingForRepairs = 19,
        LookingToRepair = 20,
        LookingToMeldMateria = 21,
        RolePlaying = 22,
        LookingForParty = 23,
        SwordForHire = 24,
        WaitingForDutyFinder = 25,
        RecruitingPartyMembers = 26,
        Mentor = 27,
        PveMentor = 28,
        TradeMentor = 29,
        PvpMentor = 30,
        Returner = 31,
        NewAdventurer = 32,
        AllianceLeader = 33,
        AlliancePartyLeader = 34,
        AlliancePartyMember = 35,
        PartyLeader = 36,
        PartyMember = 37,
        PartyLeaderCrossWorld = 38,
        PartyMemberCrossWorld = 39,
        AnotherWorld = 40,
        SharingDuty = 41,
        SimilarDuty = 42,
        InDuty = 43,
        TrialAdventurer = 44,
        FreeCompany = 45,
        GrandCompany = 46,
        Online = 47,
    }

    // ReSharper disable once IdentifierTypo
    public abstract class Encodable {
        protected abstract byte Code { get; }
        protected abstract byte[] PayloadEncode();

        public byte[] Encode() {
            byte[] payload = this.PayloadEncode();

            if (payload.Length == 0) {
                return [
                    this.Code,
                ];
            }

            byte[] bytes = new byte[1 + payload.Length];
            bytes[0] = this.Code;
            Array.Copy(payload, 0, bytes, 1, payload.Length);
            return bytes;
        }
    }

    public class MillisecondsDateTimeFormatter : IMessagePackFormatter<DateTime> {
        private static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        public DateTime Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options) {
            var millis = reader.ReadInt64();
            return Epoch.AddMilliseconds(millis);
        }

        public void Serialize(ref MessagePackWriter writer, DateTime value, MessagePackSerializerOptions options) {
            var millis = (long) (value.ToUniversalTime() - Epoch).TotalMilliseconds;
            writer.WriteInt64(millis);
        }
    }
}
