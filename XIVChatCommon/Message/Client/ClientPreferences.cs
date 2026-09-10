using System;
using System.Collections.Generic;
using System.Reflection;
using MessagePack;

namespace XIVChatCommon.Message.Client {
    [MessagePackObject]
    public class ClientPreferences : Encodable {
        [Key(0)]
        public Dictionary<ClientPreference, object> Preferences { get; set; } = new();

        [Key(1)] public ushort[]? Channels { get; set; }

        protected override byte Code => (byte) ClientOperation.Preferences;

        protected override byte[] PayloadEncode() {
            return MessagePackSerializer.Serialize(this);
        }

        public static ClientPreferences Decode(byte[] bytes) {
            return MessagePackSerializer.Deserialize<ClientPreferences>(bytes);
        }
    }

    public enum ClientPreference {
        [Preference(typeof(bool))]
        BacklogNewestMessagesFirst = 0,

        [Preference(typeof(bool))]
        TargetingListSupport = 1,
        
        [Preference(typeof(bool))]
        HousingLocationSupport = 2,

        [Preference(typeof(bool))]
        WorkbenchSupport = 3,

        [Preference(typeof(bool))]
        GuardedCommandsSupport = 4,

        [Preference(typeof(bool))]
        GameEventsSupport = 5,
    }

    public static class ClientPreferencesExtension {
        public static bool TryGetValue<T>(this ClientPreferences prefs, ClientPreference pref, out T value) {
            value = default!;

            if (!prefs.Preferences.TryGetValue(pref, out var obj)) {
                return false;
            }

            var attr = pref
                .GetType()
                .GetField(pref.ToString())
                ?.GetCustomAttribute<PreferenceAttribute>(false);

            if (obj == null || obj.GetType() != typeof(T) || obj.GetType() != attr?.ValueType) {
                return false;
            }

            value = (T) obj;
            return true;
        }
    }

    public class PreferenceAttribute : Attribute {
        public Type ValueType { get; }

        public PreferenceAttribute(Type valueType) {
            this.ValueType = valueType;
        }
    }
}
