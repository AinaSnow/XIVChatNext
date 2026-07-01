using Dalamud.Hooking;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.System.Memory;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using XIVChatCommon.Message;
using XIVChatCommon.Message.Server;
using GrandCompany = Lumina.Excel.Sheets.GrandCompany;

namespace XIVChatPlugin {
    internal unsafe class GameFunctions : IDisposable {
        private static class Signatures {
            internal const string ProcessChat = "48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 48 8B F2 48 8B F9 45 84 C9";
            internal const string Input = "E8 ?? ?? ?? ?? ?? ?? ?? 84 C0 B9";
            internal const string InputAfk = "E8 ?? ?? ?? ?? 84 C0 74 ?? 66 83 3D";
            internal const string FriendList = "40 53 48 81 EC 80 0F 00 00 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 48 8B D9 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 0F 84 ?? ?? ?? ?? 44 0F B6 43 ?? 33 C9";
            internal const string Format = "48 89 5C 24 ?? 56 57 41 56 48 83 EC 30 4C 8B 74 24";
            internal const string ReceiveChunk = "48 89 5C 24 ?? 56 48 83 EC 20 48 8B 0D ?? ?? ?? ?? 48 8B F2";

            internal const string GetColour = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B F2 48 8D B9";

            internal const string Channel = "E8 ?? ?? ?? ?? 33 C0 EB ?? 85 D2";
            internal const string ChannelCommand = "E8 ?? ?? ?? ?? 0F B7 44 37";
            internal const string ChannelNameChange = "E8 ?? ?? ?? ?? BA ?? ?? ?? ?? 48 8D 4D B0 48 8B F8 E8 ?? ?? ?? ?? 41 8B D6";
            internal const string ColourLookup = "48 8D 0D ?? ?? ?? ?? ?? ?? ?? 85 D2 7E";
        }

        private Plugin Plugin { get; }

        #region Delegates

        private delegate void EasierProcessChatBoxDelegate(nint uiModule, nint message, nint unused, byte a4);

        private delegate byte IsInputDelegate(nint a1);

        private delegate byte IsInputAfkDelegate();

        private delegate byte RequestFriendListDelegate(nint manager);

        private delegate int FormatFriendListNameDelegate(long a1, long a2, long a3, int a4, nint data, long a6);

        private delegate nint OnReceiveFriendListChunkDelegate(nint a1, nint data);

        private delegate nint GetColourInfoDelegate(nint handler, uint lookupResult);

        private delegate byte ChatChannelChangeDelegate(nint a1, uint channel);

        private delegate nint ChatChannelChangeNameDelegate(nint a1);

        private delegate nint ChannelChangeCommandDelegate(nint a1, int inputChannel, uint linkshellIdx, nint tellTarget, char canChangeChannel);

        #endregion

        #region Hooks

        [Signature(Signatures.Input, DetourName = nameof(IsInputDetour))]
        private readonly Hook<IsInputDelegate>? _isInputHook;

        [Signature(Signatures.InputAfk, DetourName = nameof(IsInputAfkDetour))]
        private readonly Hook<IsInputAfkDelegate>? _isInputAfkHook;

        [Signature(Signatures.FriendList, DetourName = nameof(OnRequestFriendList))]
        private readonly Hook<RequestFriendListDelegate>? _friendListHook;

        [Signature(Signatures.Format, DetourName = nameof(OnFormatFriendList))]
        private readonly Hook<FormatFriendListNameDelegate>? _formatHook;

        [Signature(Signatures.ReceiveChunk, DetourName = nameof(OnReceiveFriendList))]
        private readonly Hook<OnReceiveFriendListChunkDelegate>? _receiveChunkHook;

        [Signature(Signatures.Channel, DetourName = nameof(ChangeChatChannelDetour))]
        private readonly Hook<ChatChannelChangeDelegate>? _chatChannelChangeHook;

        [Signature(Signatures.ChannelNameChange, DetourName = nameof(ChangeChatChannelNameDetour))]
        private readonly Hook<ChatChannelChangeNameDelegate>? _chatChannelChangeNameHook;

        #endregion

        #region Functions

        [Signature(Signatures.ProcessChat)]
        private readonly EasierProcessChatBoxDelegate? _easierProcessChatBox;

        [Signature(Signatures.GetColour)]
        private readonly GetColourInfoDelegate? _getColourInfo;

        [Signature(Signatures.ChannelCommand)]
        private readonly ChannelChangeCommandDelegate? _channelChangeCommand;

        #endregion

        #region Pointers

        [Signature(Signatures.ColourLookup, ScanType = ScanType.StaticAddress)]
        private nint ColourLookup { get; init; }

        #endregion

        public ServerHousingLocation HousingLocation {
            get {
                var info = XIVChatPlugin.HousingLocation.Current();
                if (info == null) {
                    return new ServerHousingLocation(null, null, false, null);
                }

                var ward = info.Ward;
                var plot = info.Plot ?? info.Yard ?? info.Apartment;
                var wing = (byte?) info.ApartmentWing;
                var exterior = info.Yard != null;

                return new ServerHousingLocation(ward, plot, exterior, wing);
            }
        }

        [Flags]
        private enum InputSetters {
            None = 0,
            Normal = 1 << 0,
            Afk = 1 << 1,
        }

        private InputSetters HadInput { get; set; } = InputSetters.None;
        private nint _friendListManager = nint.Zero;
        private nint _chatManager = nint.Zero;
        private readonly nint _emptyXivString;

        internal bool RequestingFriendList { get; private set; }

        private readonly List<Player> _friends = [];

        internal delegate void ReceiveFriendListHandler(List<Player> friends);

        internal event ReceiveFriendListHandler? ReceiveFriendList;

        internal GameFunctions(Plugin plugin) {
            this.Plugin = plugin;

            this.Plugin.GameInteropProvider.InitializeFromAttributes(this);

            this._friendListHook?.Enable();
            this._formatHook?.Enable();
            this._receiveChunkHook?.Enable();
            this._chatChannelChangeHook?.Enable();
            this._chatChannelChangeNameHook?.Enable();
            this._isInputHook?.Enable();
            this._isInputAfkHook?.Enable();

            this._emptyXivString = (nint) Utf8String.CreateEmpty();
        }

        private byte IsInputDetour(nint a1) {
            if (!this.Plugin.Config.MessagesCountAsInput || this.HadInput == InputSetters.None) {
                return this._isInputHook!.Original(a1);
            }

            this.HadInput &= ~InputSetters.Normal;
            return 1;
        }

        private byte IsInputAfkDetour() {
            if (!this.Plugin.Config.MessagesCountAsInput || this.HadInput == InputSetters.None) {
                return this._isInputAfkHook!.Original();
            }

            this.HadInput &= ~InputSetters.Afk;
            return 1;
        }

        internal void ChangeChatChannel(InputChannel channel) {
            if (this._chatManager == nint.Zero || this._channelChangeCommand == null || this._emptyXivString == nint.Zero) {
                return;
            }

            this._channelChangeCommand(this._chatManager, (int) channel, channel.LinkshellIndex(), this._emptyXivString, '\x01');
        }

        // This function looks up a channel's user-defined colour.
        //
        // If this function would ever return 0, it returns null instead.
        internal uint? GetChannelColour(ChatCode channel) {
            if (this._getColourInfo == null || this.ColourLookup == nint.Zero) {
                return null;
            }

            // Colours are retrieved by looking up their code in a lookup table. Some codes share a colour, so they're lumped into a parent code here.
            // Only codes >= 10 (say) have configurable colours.
            // After getting the lookup value for the code, it is passed into a function with a handler which returns a pointer.
            // This pointer + 32 is the RGB value. This functions returns RGBA with A always max.

            var parent = channel.Parent();

            switch (parent) {
                case ChatType.Debug:
                case ChatType.Urgent:
                case ChatType.Notice:
                    return channel.DefaultColour();
            }

            var framework = (nint) Framework.Instance();

            var lookupResult = *(uint*) (this.ColourLookup + (int) parent * 4);
            var info = this._getColourInfo(framework + 16, lookupResult);
            var rgb = *(uint*) (info + 32) & 0xFFFFFF;

            if (rgb == 0) {
                return null;
            }

            return 0xFF | (rgb << 8);
        }

        internal void ProcessChatBox(string message) {
            if (this._easierProcessChatBox == null) {
                return;
            }

            this.HadInput = InputSetters.Normal | InputSetters.Afk;

            var uiModule = UIModule.Instance();

            using var payload = new ChatPayload(message);
            var mem1 = Marshal.AllocHGlobal(400);
            Marshal.StructureToPtr(payload, mem1, false);

            this._easierProcessChatBox((nint) uiModule, mem1, nint.Zero, 0);

            Marshal.FreeHGlobal(mem1);
        }

        internal bool RequestFriendList() {
            if (this._friendListManager == nint.Zero || this._friendListHook == null) {
                return false;
            }

            this.RequestingFriendList = true;
            this._friendListHook.Original(this._friendListManager);
            return true;
        }

        private byte ChangeChatChannelDetour(nint a1, uint channel) {
            this._chatManager = a1;
            // Last ShB patch
            // a1 + 0xfd0 is the chat channel byte (including for when clicking on shout)
            return this._chatChannelChangeHook!.Original(a1, channel);
        }

        private nint ChangeChatChannelNameDetour(nint a1) {
            // Last ShB patch
            // +0x40 = chat channel (byte or uint?)
            //         channel is 17 (maybe 18?) for tells
            // +0x48 = pointer to channel name string
            var ret = this._chatChannelChangeNameHook!.Original(a1);
            if (a1 == nint.Zero) {
                return ret;
            }

            var agent = AgentChatLog.Instance();
            var channel = (uint) agent->CurrentChannel;
            var label = SeString.Parse(agent->ChannelLabel.AsSpan());

            if (channel is 17 or 18) {
                channel = 0;
            }

            this.Plugin.Server.OnChatChannelChange(channel, label);

            return ret;
        }

        private byte OnRequestFriendList(nint manager) {
            this._friendListManager = manager;
            // NOTE: if this is being called, hook isn't null
            return this._friendListHook!.Original(manager);
        }

        private int OnFormatFriendList(long a1, long a2, long a3, int a4, nint data, long a6) {
            return this._formatHook!.Original(a1, a2, a3, a4, data, a6);
        }

        private unsafe nint OnReceiveFriendList(nint a1, nint data) {
            // NOTE: if this is being called, hook isn't null
            var ret = this._receiveChunkHook!.Original(a1, data);

            if (!this.RequestingFriendList) {
                goto Return;
            }

            try {
                // data IS the InfoProxyCommonList pointer.
                // We read fields directly based on FFXIVClientStructs/InfoProxyInterface layout.
                // EntryCount at 0x10 (from InfoProxyInterface).
                var entryCount = *(uint*)(data + 0x10);
                
                // CharData pointer at 0xB0 (from InfoProxyCommonList).
                // This points to the start of the array of CharacterData structures.
                var charDataPtr = *(byte**)(data + 0xB0);

                if (entryCount > 2000 || charDataPtr == null) {
                    goto Return;
                }

                this._friends.Clear();
                var sizeOfEntry = 0x70; // sizeof(CharacterData)

                for (uint i = 0; i < entryCount; i++) {
                    var entryPtr = charDataPtr + (i * sizeOfEntry);
                    // Parse the entry from the array
                    var entry = Marshal.PtrToStructure<FriendListEntry>((nint)entryPtr);
                    
                    if (entry.ContentId == 0) continue; // Skip empty/invalid

                    string? jobName = null;
                    if (entry.Job > 0) {
                        jobName = this.Plugin.DataManager.GetExcelSheet<ClassJob>().GetRowOrDefault(entry.Job)?.Name.ExtractText();
                    }

                    string? territoryName = null;
                    try {
                        territoryName = this.Plugin.DataManager.GetExcelSheet<TerritoryType>().GetRowOrDefault(entry.TerritoryId)?.PlaceName.Value.Name.ExtractText();
                    } catch (NullReferenceException) {
                        territoryName = null;
                    }

                    var player = new Player {
                        Name = entry.Name(),
                        FreeCompany = entry.FreeCompany(),
                        Status = entry.Status,

                        CurrentWorld = entry.CurrentWorldId,
                        CurrentWorldName = this.Plugin.DataManager.GetExcelSheet<World>().GetRowOrDefault(entry.CurrentWorldId)?.Name.ExtractText(),
                        HomeWorld = entry.HomeWorldId,
                        HomeWorldName = this.Plugin.DataManager.GetExcelSheet<World>().GetRowOrDefault(entry.HomeWorldId)?.Name.ExtractText(),

                        Territory = entry.TerritoryId,
                        TerritoryName = territoryName,

                        Job = entry.Job,
                        JobName = jobName,

                        GrandCompany = entry.GrandCompany,
                        GrandCompanyName = this.Plugin.DataManager.GetExcelSheet<GrandCompany>().GetRowOrDefault(entry.GrandCompany)?.Name.ExtractText(),

                        Languages = entry.LangsEnabled,
                        MainLanguage = entry.MainLanguage,
                    };
                    this._friends.Add(player);
                }

                this.ReceiveFriendList?.Invoke(this._friends);

            } catch (Exception ex) {
                Plugin.Log.Error(ex, "Error processing friend list");
            }

            Return:
            // reset properly
            this.RequestingFriendList = false;

            return ret;
        }


    [StructLayout(LayoutKind.Explicit, Size = 0x70)]
    internal unsafe struct FriendListEntry {
        [FieldOffset(0x00)] internal ulong ContentId;
        [FieldOffset(0x08)] internal ulong Status;
        
        [FieldOffset(0x20)] internal uint ExtraFlags;
        
        [FieldOffset(0x26)] internal ushort CurrentWorldId;
        [FieldOffset(0x28)] internal ushort HomeWorldId;
        [FieldOffset(0x2A)] internal ushort TerritoryId;
        
        [FieldOffset(0x2C)] internal byte GrandCompany;
        [FieldOffset(0x2D)] internal byte MainLanguage;
        [FieldOffset(0x2E)] internal byte LangsEnabled;
        
        [FieldOffset(0x31)] internal byte Job;
        
        [FieldOffset(0x32)] internal fixed byte name[32];
        [FieldOffset(0x52)] internal fixed byte fc[14]; 

        internal string? Name() {
            fixed (byte* p = this.name) {
                return HandleString(p, 32);
            }
        }
        internal string? FreeCompany() {
             fixed (byte* p = this.fc) {
                 return HandleString(p, 14);
             }
        }

        private static string? HandleString(byte* ptr, int len) {
             int count = 0;
             while (count < len && ptr[count] != 0) {
                 count++;
             }
             if (count == 0) return null;
             return Encoding.UTF8.GetString(ptr, count);
        }
    }
    
    // I will insert the struct at the end of file properly.
    // Here I only replace the method body.
    // I need to use write_to_file or multi_replace to put the struct at EOF.


        public void Dispose() {
            this._friendListHook?.Dispose();
            this._formatHook?.Dispose();
            this._receiveChunkHook?.Dispose();
            this._chatChannelChangeHook?.Dispose();
            this._chatChannelChangeNameHook?.Dispose();
            this._isInputHook?.Dispose();
            this._isInputAfkHook?.Dispose();

            if (this._emptyXivString != nint.Zero) {
                var str = (Utf8String*) this._emptyXivString;
                str->Dtor();
                IMemorySpace.Free(str);
            }
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    [SuppressMessage("ReSharper", "PrivateFieldCanBeConvertedToLocalVariable")]
    internal readonly struct ChatPayload : IDisposable {
        [FieldOffset(0)]
        private readonly IntPtr textPtr;

        [FieldOffset(16)]
        private readonly ulong textLen;

        [FieldOffset(8)]
        private readonly ulong unk1;

        [FieldOffset(24)]
        private readonly ulong unk2;

        internal ChatPayload(string text) {
            var stringBytes = Encoding.UTF8.GetBytes(text);
            this.textPtr = Marshal.AllocHGlobal(stringBytes.Length + 30);
            Marshal.Copy(stringBytes, 0, this.textPtr, stringBytes.Length);
            Marshal.WriteByte(this.textPtr + stringBytes.Length, 0);

            this.textLen = (ulong) (stringBytes.Length + 1);

            this.unk1 = 64;
            this.unk2 = 0;
        }

        public void Dispose() {
            Marshal.FreeHGlobal(this.textPtr);
        }
    }


}
