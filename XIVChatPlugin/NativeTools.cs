using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace XIVChatPlugin {
    internal static unsafe class NativeTools {
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct RawVec {
            public readonly byte** pointer;
            public readonly uint length;
            private readonly uint capacity;
        }

        [DllImport("xivchat_native_tools.dll")]
        private static extern RawVec* wrap(byte* input, uint width);

        [DllImport("xivchat_native_tools.dll")]
        private static extern void wrap_free(RawVec* raw);

        internal static IEnumerable<string> Wrap(string input, uint width) {
            RawVec* raw;
            fixed (byte* ptr = Encoding.UTF8.GetBytes(input).Terminate()) {
                raw = wrap(ptr, width);
            }

            if (raw == null) {
                return Array.Empty<string>();
            }

            var strings = new List<string>((int) raw->length);
            for (var i = 0; i < raw->length; i++) {
                var bytes = Util.ReadTerminated(raw->pointer[i]);
                strings.Add(Encoding.UTF8.GetString(bytes));
            }

            wrap_free(raw);

            return strings;
        }
    }
}
