using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XIVChatCommon.Message;

namespace XIVChat_Desktop {
    public enum FilterCategory {
        [Category("Chat",
            FilterType.Say,
            FilterType.Yell,
            FilterType.Shout,
            FilterType.Tell,
            FilterType.Party,
            FilterType.Alliance,
            FilterType.FreeCompany,
            FilterType.PvpTeam,
            FilterType.CrossLinkshell1,
            FilterType.CrossLinkshell2,
            FilterType.CrossLinkshell3,
            FilterType.CrossLinkshell4,
            FilterType.CrossLinkshell5,
            FilterType.CrossLinkshell6,
            FilterType.CrossLinkshell7,
            FilterType.CrossLinkshell8,
            FilterType.Linkshell1,
            FilterType.Linkshell2,
            FilterType.Linkshell3,
            FilterType.Linkshell4,
            FilterType.Linkshell5,
            FilterType.Linkshell6,
            FilterType.Linkshell7,
            FilterType.Linkshell8,
            FilterType.NoviceNetwork,
            FilterType.StandardEmote,
            FilterType.CustomEmote,
            FilterType.Gm
        )]
        Chat,

        [Category("Battle", FilterType.Battle)]
        Battle,

        [Category("Announcements",
            FilterType.Debug,
            FilterType.Urgent,
            FilterType.Notice,
            FilterType.SystemMessages,
            FilterType.OwnBattleSystem,
            FilterType.OthersBattleSystem,
            FilterType.GatheringSystem,
            FilterType.ErrorMessages,
            FilterType.Echo,
            FilterType.NoviceNetworkAnnouncements,
            FilterType.FreeCompanyAnnouncements,
            FilterType.PvpTeamAnnouncements,
            FilterType.FreeCompanyLoginLogout,
            FilterType.PvpTeamLoginLogout,
            FilterType.RetainerSale,
            FilterType.NpcDialogue,
            FilterType.NpcAnnouncement,
            FilterType.Loot,
            FilterType.OwnProgression,
            FilterType.PartyProgression,
            FilterType.OthersProgression,
            FilterType.OwnLoot,
            FilterType.OthersLoot,
            FilterType.OwnCrafting,
            FilterType.OthersCrafting,
            FilterType.OwnGathering,
            FilterType.OthersFishing,
            FilterType.PeriodicRecruitment,
            FilterType.Sign,
            FilterType.Random,
            FilterType.Orchestrion,
            FilterType.MessageBook,
            FilterType.Alarm
        )]
        Announcements,
    }

    public class CategoryAttribute : Attribute {
        public string Name { get; }
        public FilterType[] Types { get; }

        public CategoryAttribute(string name, params FilterType[] types) {
            this.Name = name;
            this.Types = types;
        }
    }

    public static class FilterCategoryExtensions {
        private static CategoryAttribute? Info(this FilterCategory filter) => filter
            .GetType()
            .GetField(filter.ToString())
            ?.GetCustomAttribute<CategoryAttribute>(false);

        public static string? Name(this FilterCategory category) => category.Info()?.Name;

        public static IEnumerable<FilterType> Types(this FilterCategory category) => category.Info()?.Types ?? new FilterType[0];
    }

    // NOTE: Changing the order of these is a breaking change
    public enum FilterType {
        [Filter("Say", ChatType.Say)]
        Say,

        [Filter("Shout", ChatType.Shout)]
        Shout,

        [Filter("Yell", ChatType.Yell)]
        Yell,

        [Filter("Tell", ChatType.TellOutgoing, ChatType.TellIncoming)]
        Tell,

        [Filter("Party", ChatType.Party, ChatType.CrossParty)]
        Party,

        [Filter("Alliance", ChatType.Alliance)]
        Alliance,

        [Filter("Free Company", ChatType.FreeCompany)]
        FreeCompany,

        [Filter("PvP Team", ChatType.PvpTeam)]
        PvpTeam,

        [Filter("Cross-world Linkshell [1]", ChatType.CrossLinkshell1)]
        CrossLinkshell1,

        [Filter("Cross-world Linkshell [2]", ChatType.CrossLinkshell2)]
        CrossLinkshell2,

        [Filter("Cross-world Linkshell [3]", ChatType.CrossLinkshell3)]
        CrossLinkshell3,

        [Filter("Cross-world Linkshell [4]", ChatType.CrossLinkshell4)]
        CrossLinkshell4,

        [Filter("Cross-world Linkshell [5]", ChatType.CrossLinkshell5)]
        CrossLinkshell5,

        [Filter("Cross-world Linkshell [6]", ChatType.CrossLinkshell6)]
        CrossLinkshell6,

        [Filter("Cross-world Linkshell [7]", ChatType.CrossLinkshell7)]
        CrossLinkshell7,

        [Filter("Cross-world Linkshell [8]", ChatType.CrossLinkshell8)]
        CrossLinkshell8,

        [Filter("Linkshell [1]", ChatType.Linkshell1)]
        Linkshell1,

        [Filter("Linkshell [2]", ChatType.Linkshell2)]
        Linkshell2,

        [Filter("Linkshell [3]", ChatType.Linkshell3)]
        Linkshell3,

        [Filter("Linkshell [4]", ChatType.Linkshell4)]
        Linkshell4,

        [Filter("Linkshell [5]", ChatType.Linkshell5)]
        Linkshell5,

        [Filter("Linkshell [6]", ChatType.Linkshell6)]
        Linkshell6,

        [Filter("Linkshell [7]", ChatType.Linkshell7)]
        Linkshell7,

        [Filter("Linkshell [8]", ChatType.Linkshell8)]
        Linkshell8,

        [Filter("Novice Network", ChatType.NoviceNetwork)]
        NoviceNetwork,

        [Filter("Standard Emotes", ChatType.StandardEmote)]
        StandardEmote,

        [Filter("Custom Emotes", ChatType.CustomEmote)]
        CustomEmote,

        [Filter("Battle",
            ChatType.Damage,
            ChatType.Miss,
            ChatType.Action,
            ChatType.Item,
            ChatType.Healing,
            ChatType.GainBuff,
            ChatType.LoseBuff,
            ChatType.GainDebuff,
            ChatType.LoseDebuff,
            ChatType.BattleSystem
        )]
        Battle,

        [Filter("Debug", ChatType.Debug)]
        Debug,

        [Filter("Urgent", ChatType.Urgent)]
        Urgent,

        [Filter("Notice", ChatType.Notice)]
        Notice,

        [Filter("System Messages", ChatType.System)]
        SystemMessages,

        [Filter("Own Battle System Messages", ChatType.BattleSystem, Source = FilterSource.Self)]
        OwnBattleSystem,

        [Filter("Others' Battle System Messages", ChatType.BattleSystem, Source = FilterSource.Others)]
        OthersBattleSystem,

        [Filter("Gathering System Messages", ChatType.GatheringSystem)]
        GatheringSystem,

        [Filter("Error Messages", ChatType.Error)]
        ErrorMessages,

        [Filter("Echo", ChatType.Echo)]
        Echo,

        [Filter("Novice Network Notifications", ChatType.NoviceNetworkSystem)]
        NoviceNetworkAnnouncements,

        [Filter("Free Company Announcements", ChatType.FreeCompanyAnnouncement)]
        FreeCompanyAnnouncements,

        [Filter("PvP Team Announcements", ChatType.PvpTeamAnnouncement)]
        PvpTeamAnnouncements,

        [Filter("Free Company Member Login Notifications", ChatType.FreeCompanyLoginLogout)]
        FreeCompanyLoginLogout,

        [Filter("PvP Team Member Login Notifications", ChatType.PvpTeamLoginLogout)]
        PvpTeamLoginLogout,

        [Filter("Retainer Sale Notifications", ChatType.RetainerSale)]
        RetainerSale,

        [Filter("NPC Dialogue", ChatType.NpcDialogue)]
        NpcDialogue,

        [Filter("NPC Dialogue (Announcements)", ChatType.NpcAnnouncement)]
        NpcAnnouncement,

        [Filter("Loot Notices", ChatType.LootNotice)]
        Loot,

        [Filter("Own Progression Messages", ChatType.Progress, Source = FilterSource.Self)]
        OwnProgression,

        [Filter("Party Members' Progression Messages", ChatType.Progress, Source = FilterSource.Party)]
        PartyProgression,

        [Filter("Others' Progression Messages", ChatType.Progress, Source = FilterSource.Others)]
        OthersProgression,

        [Filter("Own Loot Messages", ChatType.LootRoll, Source = FilterSource.Self)]
        OwnLoot,

        [Filter("Others' Loot Messages", ChatType.LootRoll, Source = FilterSource.Others)]
        OthersLoot,

        [Filter("Own Synthesis Messages", ChatType.Crafting, Source = FilterSource.Self)]
        OwnCrafting,

        [Filter("Others' Synthesis Messages", ChatType.Crafting, Source = FilterSource.Others)]
        OthersCrafting,

        [Filter("Own Gathering Messages", ChatType.Gathering, Source = FilterSource.Self)]
        OwnGathering,

        [Filter("Others' Fishing Messages", ChatType.Gathering, Source = FilterSource.Others)]
        OthersFishing,

        [Filter("Periodic Recruitment Notifications", ChatType.PeriodicRecruitmentNotification)]
        PeriodicRecruitment,

        [Filter("Sign Messages for PC Targets", ChatType.Sign)]
        Sign,

        [Filter("Random Number Messages", ChatType.RandomNumber)]
        Random,

        [Filter("Current Orchestrion Track Messages", ChatType.Orchestrion)]
        Orchestrion,

        [Filter("Message Book Alert", ChatType.MessageBook)]
        MessageBook,

        [Filter("Alarm Notifications", ChatType.Alarm)]
        Alarm,

        [Filter("GM Messages",
            ChatType.GmTell,
            ChatType.GmSay,
            ChatType.GmShout,
            ChatType.GmYell,
            ChatType.GmParty,
            ChatType.GmFreeCompany,
            ChatType.GmLinkshell1,
            ChatType.GmLinkshell2,
            ChatType.GmLinkshell3,
            ChatType.GmLinkshell4,
            ChatType.GmLinkshell5,
            ChatType.GmLinkshell6,
            ChatType.GmLinkshell7,
            ChatType.GmLinkshell8,
            ChatType.GmNoviceNetwork
        )]
        Gm,
    }

    public enum FilterSource {
        None,
        Self,
        Party,
        Others,
    }

    public class FilterAttribute : Attribute {
        public string Name { get; }
        public ChatType[] Types { get; }
        public FilterSource Source { get; set; } = FilterSource.None;

        public FilterAttribute(string name, params ChatType[] types) {
            this.Name = name;
            this.Types = types;
        }
    }

    public static class FilterTypeExtensions {
        private static readonly ChatSource[] Others = {
            ChatSource.PartyMember, ChatSource.AllianceMember, ChatSource.Other, ChatSource.EngagedEnemy, ChatSource.UnengagedEnemy, ChatSource.FriendlyNpc, ChatSource.PartyPet, ChatSource.AlliancePet, ChatSource.OtherPet,
        };

        private static FilterAttribute? Info(this FilterType filter) => filter
            .GetType()
            .GetField(filter.ToString())
            ?.GetCustomAttribute<FilterAttribute>(false);

        public static string? Name(this FilterType filter) => filter.Info()?.Name;

        public static IEnumerable<ChatType> Types(this FilterType filter) => filter.Info()?.Types ?? new ChatType[0];

        private static ChatSource[] Sources(this FilterType filter) => filter.Info()?.Source switch {
            FilterSource.Self => new[] {
                ChatSource.Self,
            },
            FilterSource.Party => new[] {
                ChatSource.PartyMember,
            },
            FilterSource.Others => Others,
            _ => new ChatSource[0],
        };

        public static bool Allowed(this FilterType filter, ChatCode code) {
            if (!filter.Types().Contains(code.Type)) {
                return false;
            }

            var sources = filter.Sources();
            return sources.Length == 0 || sources.Contains(code.Source);
        }
    }
}
