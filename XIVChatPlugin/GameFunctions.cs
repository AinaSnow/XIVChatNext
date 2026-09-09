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
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
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

            internal const string GetColour = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 8B F2 48 8D B9";

            internal const string ChannelNameChange = "E8 ?? ?? ?? ?? BA ?? ?? ?? ?? 48 8D 4D B0 48 8B F8 E8 ?? ?? ?? ?? 41 8B D6";
            internal const string ColourLookup = "48 8D 0D ?? ?? ?? ?? ?? ?? ?? 85 D2 7E";
        }

        private Plugin Plugin { get; }

        #region Delegates

        private delegate void EasierProcessChatBoxDelegate(nint uiModule, nint message, nint unused, byte a4);

        private delegate byte IsInputDelegate(nint a1);

        private delegate byte IsInputAfkDelegate();

        private delegate nint GetColourInfoDelegate(nint handler, uint lookupResult);


        private delegate nint ChatChannelChangeNameDelegate(nint a1);


        #endregion

        #region Hooks

        [Signature(Signatures.Input, DetourName = nameof(IsInputDetour))]
        private readonly Hook<IsInputDelegate>? _isInputHook;

        [Signature(Signatures.InputAfk, DetourName = nameof(IsInputAfkDetour))]
        private readonly Hook<IsInputAfkDelegate>? _isInputAfkHook;


        [Signature(Signatures.ChannelNameChange, DetourName = nameof(ChangeChatChannelNameDetour))]
        private readonly Hook<ChatChannelChangeNameDelegate>? _chatChannelChangeNameHook;

        #endregion

        #region Functions

        [Signature(Signatures.ProcessChat)]
        private readonly EasierProcessChatBoxDelegate? _easierProcessChatBox;

        [Signature(Signatures.GetColour)]
        private readonly GetColourInfoDelegate? _getColourInfo;


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
        private readonly nint _emptyXivString;

        internal GameFunctions(Plugin plugin) {
            this.Plugin = plugin;

            this.Plugin.GameInteropProvider.InitializeFromAttributes(this);
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

        internal bool ChangeChatChannel(InputChannel channel) {
            if (!this.Plugin.Framework.IsInFrameworkUpdateThread) return false;
            var shell = RaptureShellModule.Instance();
            if (shell == null || this._emptyXivString == nint.Zero) return false;
            return shell->ChangeChatChannel((int)channel, channel.LinkshellIndex(), (Utf8String*)this._emptyXivString, true);
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

        private nint ChangeChatChannelNameDetour(nint a1) {
            var ret = this._chatChannelChangeNameHook!.Original(a1);
            this.RefreshChatChannel();
            return ret;
        }

        internal bool RefreshChatChannel() {
            if (!this.Plugin.Framework.IsInFrameworkUpdateThread) return false;
            var agent = AgentChatLog.Instance();
            if (agent == null) return false;
            var channel = (uint) agent->CurrentChannel;
            var label = SeString.Parse(agent->ChannelLabel.AsSpan());

            if (channel is 17 or 18) {
                channel = 0;
            }

            var target = channel == 0 ? agent->TellPlayerName.ToString() + "@" + agent->TellWorldId : null;
            this.Plugin.Server?.OnChatChannelChange(channel, label, target);
            return true;
        }

        public void Dispose() {
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
