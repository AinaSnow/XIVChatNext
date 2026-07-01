using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;

namespace XIVChatPlugin {
    internal static class Util {
        internal static string ToHexString(this IEnumerable<byte> bytes, bool upper = false, string separator = "") {
            return string.Join(separator, bytes.Select(b => b.ToString(upper ? "X2" : "x2")));
        }

        internal static List<Vector4> ToColours(this byte[] bytes) {
            var colours = new List<Vector4>();

            var colour = new Vector4(0f, 0f, 0f, 1f);
            for (var i = 0; i < bytes.Length; i++) {
                var idx = i % 3;

                if (i != 0 && idx == 0) {
                    colours.Add(colour);
                    colour = new Vector4(0f, 0f, 0f, 1f);
                }

                switch (idx) {
                    case 0:
                        colour.X = bytes[i] / 255f;
                        break;
                    case 1:
                        colour.Y = bytes[i] / 255f;
                        break;
                    case 2:
                        colour.Z = bytes[i] / 255f;
                        break;
                    default:
                        throw new ApplicationException("unreachable code reached");
                }
            }

            colours.Add(colour);

            return colours;
        }

        internal static int IndexOfCount(this string source, char toFind, int position) {
            var index = -1;
            for (var i = 0; i < position; i++) {
                index = source.IndexOf(toFind, index + 1);

                if (index == -1) {
                    return -1;
                }
            }

            return index;
        }

        internal static byte[] Terminate(this byte[] bytes) {
            var terminated = new byte[bytes.Length + 1];
            Array.Copy(bytes, terminated, bytes.Length);
            terminated[terminated.Length - 1] = 0;
            return terminated;
        }

        internal static unsafe byte[] ReadTerminated(byte* mem) {
            var bytes = new List<byte>();
            while (*mem != 0) {
                bytes.Add(*mem);
                mem += 1;
            }

            return bytes.ToArray();
        }

        internal static nint FollowPointerChain(nint start, IEnumerable<int> offsets) {
            if (start == nint.Zero) {
                return nint.Zero;
            }

            // Plugin.Log.Info($"start: {start.ToInt64():x}");

            foreach (var offset in offsets) {
                start = Marshal.ReadIntPtr(start + offset);
                // Plugin.Log.Info($"  + {offset}: {start.ToInt64():x}");
                if (start == nint.Zero) {
                    return nint.Zero;
                }
            }

            return start;
        }
    }
}
