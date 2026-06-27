using System;
using System.Collections.Generic;
using System.Numerics;
using OriathHub.Utils;
using GameOffsets.Natives;

namespace OriathHub.Plugins.Ritual
{
    public class RitualItemReward
    {
        public IntPtr EntityAddress { get; set; }
        public string Path { get; set; }
        public Vector2 Position { get; set; }
        public Vector2 Size { get; set; }
        public string InternalName { get; set; }
    }

    public static class RitualUiParser
    {
        private const int ItemEntityOffset = 0x4F8;
        private const int TextOffset = 0x390;
        private const int ParentOffset = 0xB8;

        public static string SearchStatus = "Oczekiwanie...";
        public static IntPtr ManualGridAddress = IntPtr.Zero;
        public static List<string> ProbeLogs = new List<string>();

        private static DateTime _lastSearchTime = DateTime.MinValue;
        private static IntPtr _cachedAnchor = IntPtr.Zero;
        public static IntPtr LastFoundGridAddress = IntPtr.Zero;

        public static void ClearCache(string reason)
        {
            _cachedAnchor = IntPtr.Zero;
            LastFoundGridAddress = IntPtr.Zero;
            SearchStatus = reason;
            CurrentRewards.Clear();
        }
        public static List<RitualItemReward> CurrentRewards = new();

        public static List<RitualItemReward> GetVisibleRewards()
        {
            var inGame = Core.States.InGameStateObject;
            if (inGame == null) return new List<RitualItemReward>();

            if ((DateTime.Now - _lastSearchTime).TotalMilliseconds < 16 && CurrentRewards.Count > 0)
            {
                return CurrentRewards;
            }

            _lastSearchTime = DateTime.Now;
            var rewards = new List<RitualItemReward>();

            if (_cachedAnchor != IntPtr.Zero)
            {
                if (!IsElementValidAndVisible(_cachedAnchor))
                {
                    _cachedAnchor = IntPtr.Zero;
                    LastFoundGridAddress = IntPtr.Zero;
                }
            }

            if (_cachedAnchor == IntPtr.Zero)
            {
                _cachedAnchor = FindElementWithTextRaw(inGame.GameUi.Address, "Favours", 0);
            }

            if (_cachedAnchor == IntPtr.Zero)
            {
                SearchStatus = "Nie znaleziono napisu 'Favours'";
                return rewards;
            }

            LastFoundGridAddress = GetGridByRelativePath(_cachedAnchor);

            if (LastFoundGridAddress == IntPtr.Zero)
            {
                SearchStatus = "Błąd ścieżki: Parent -> 13th child nie istnieje";
                return rewards;
            }

            SearchStatus = "OK - Ritual Wykryty";

            if (Core.Process.ReadMemory<IntPtr>(LastFoundGridAddress + 0x10, out var firstChild) &&
                Core.Process.ReadMemory<IntPtr>(LastFoundGridAddress + 0x18, out var lastChild))
            {
                int count = (int)((lastChild.ToInt64() - firstChild.ToInt64()) / 8);
                float winW = Core.Process.WindowArea.Width;
                float winH = Core.Process.WindowArea.Height;

                for (int i = 0; i < Math.Min(count, 100); i++)
                {
                    if (Core.Process.ReadMemory<IntPtr>(firstChild + (i * 8), out var tileAddr) && tileAddr != IntPtr.Zero)
                    {
                        if (Core.Process.ReadMemory<IntPtr>(tileAddr + ItemEntityOffset, out var entityAddr) && entityAddr != IntPtr.Zero)
                        {
                            string path = GetEntityPath(entityAddr);
                            if (string.IsNullOrEmpty(path)) continue;

                            GetCorrectUiRect(tileAddr, winW, winH, out var pos, out var size);
                            rewards.Add(new RitualItemReward
                            {
                                EntityAddress = entityAddr,
                                Path = path,
                                Position = pos,
                                Size = size,
                                InternalName = path.Substring(path.LastIndexOf('/') + 1)
                            });
                        }
                    }
                }
            }
            CurrentRewards = rewards;
            return rewards;
        }

        private static bool IsElementValidAndVisible(IntPtr addr)
        {
            if (addr == IntPtr.Zero) return false;
            if (Core.Process.ReadMemory<uint>(addr + 0x180, out var flags))
                return (flags & (1u << 0x0B)) != 0;
            return false;
        }

        private static IntPtr FindElementWithTextRaw(IntPtr parent, string targetText, int depth)
        {
            if (depth > 25 || parent == IntPtr.Zero) return IntPtr.Zero;

            if (Core.Process.ReadMemory<StdWString>(parent + TextOffset, out var stdW))
            {
                var text = Core.Process.ReadStdWString(stdW);
                if (!string.IsNullOrEmpty(text) && text.Equals(targetText, StringComparison.OrdinalIgnoreCase))
                    return parent;
            }

            if (Core.Process.ReadMemory<IntPtr>(parent + 0x10, out var first) &&
                Core.Process.ReadMemory<IntPtr>(parent + 0x18, out var last))
            {
                int count = (int)((last.ToInt64() - first.ToInt64()) / 8);
                for (int i = 0; i < Math.Min(count, 1000); i++)
                {
                    if (Core.Process.ReadMemory<IntPtr>(first + (i * 8), out var child) && child != IntPtr.Zero)
                    {
                        var found = FindElementWithTextRaw(child, targetText, depth + 1);
                        if (found != IntPtr.Zero) return found;
                    }
                }
            }
            return IntPtr.Zero;
        }

        private static IntPtr GetGridByRelativePath(IntPtr anchor)
        {
            if (!Core.Process.ReadMemory<IntPtr>(anchor + ParentOffset, out var parentAddr)) return IntPtr.Zero;
            if (!Core.Process.ReadMemory<IntPtr>(parentAddr + 0x10, out var f) ||
                !Core.Process.ReadMemory<IntPtr>(parentAddr + 0x18, out var l)) return IntPtr.Zero;

            long childrenCount = (l.ToInt64() - f.ToInt64()) / 8;
            if (childrenCount > 13)
            {
                if (Core.Process.ReadMemory<IntPtr>(f + (13 * 8), out var grid)) return grid;
            }
            return IntPtr.Zero;
        }

        private static void GetCorrectUiRect(IntPtr el, float winW, float winH, out Vector2 pos, out Vector2 size)
        {
            Core.Process.ReadMemory<float>(el + 0x130, out var multiplier);
            Core.Process.ReadMemory<byte>(el + 0x18A, out var scaleIndex);
            Core.Process.ReadMemory<Vector2>(el + 0x288, out size);
            if (multiplier == 0) multiplier = 1.0f;

            Vector2 unscaledPos = GetUnscaledPos(el, 0);

            float sw = multiplier, sh = multiplier;
            float v1 = winW / 2560.0f, v2 = winH / 1600.0f;
            switch (scaleIndex)
            {
                case 1: sw *= v1; sh *= v1; break;
                case 2: sw *= v2; sh *= v2; break;
                case 3: sw *= v1; sh *= v2; break;
            }
            pos = new Vector2(unscaledPos.X * sw, unscaledPos.Y * sh);
            size = new Vector2(size.X * sw, size.Y * sh);
        }

        private static Vector2 GetUnscaledPos(IntPtr el, int depth)
        {
            if (el == IntPtr.Zero || depth >= 64) return Vector2.Zero;
            Core.Process.ReadMemory<Vector2>(el + 0x118, out var relPos);
            Core.Process.ReadMemory<IntPtr>(el + ParentOffset, out var parent);
            if (parent == IntPtr.Zero) return relPos;
            Vector2 parentPos = GetUnscaledPos(parent, depth + 1);
            Core.Process.ReadMemory<uint>(el + 0x180, out var flags);
            if ((flags & (1u << 0x0A)) != 0)
            {
                Core.Process.ReadMemory<Vector2>(el + 0xF0, out var modifier);
                parentPos += modifier;
            }
            return parentPos + relPos;
        }

        public static IntPtr ResolveComponent(IntPtr entityAddr, string name)
        {
            if (entityAddr == IntPtr.Zero) return IntPtr.Zero;
            if (!Core.Process.ReadMemory<IntPtr>(entityAddr + 0x08, out var d)) return IntPtr.Zero;
            if (!Core.Process.ReadMemory<IntPtr>(d + 0x28, out var l)) return IntPtr.Zero;
            if (!Core.Process.ReadMemory<IntPtr>(entityAddr + 0x10, out var cf)) return IntPtr.Zero;
            if (!Core.Process.ReadMemory<IntPtr>(entityAddr + 0x18, out var cl)) return IntPtr.Zero;
            long cnt = (cl.ToInt64() - cf.ToInt64()) / 8;
            if (!Core.Process.ReadMemory<IntPtr>(l + 0x28, out var bf)) return IntPtr.Zero;
            if (!Core.Process.ReadMemory<IntPtr>(l + 0x30, out var bl)) return IntPtr.Zero;
            long bc = (bl.ToInt64() - bf.ToInt64()) / 16;
            for (int i = 0; i < bc; i++)
            {
                IntPtr e = bf + (i * 16);
                if (Core.Process.ReadMemory<IntPtr>(e, out var np))
                {
                    byte[] nb = Core.Process.ReadMemoryArrayRequired<byte>(np, 32);
                    string cn = System.Text.Encoding.ASCII.GetString(nb).Split('\0')[0];
                    if (cn == name && Core.Process.ReadMemory<int>(e + 0x08, out var idx))
                    {
                        if (idx >= 0 && idx < cnt)
                        {
                            if (Core.Process.ReadMemory<IntPtr>(cf + (idx * 8), out var res)) return res;
                        }
                    }
                }
            }
            return IntPtr.Zero;
        }

        public static string GetEntityPath(IntPtr entityAddr)
        {
            if (Core.Process.ReadMemory<IntPtr>(entityAddr + 0x08, out var d))
            {
                if (Core.Process.ReadMemory<StdWString>(d + 0x08, out var s)) return Core.Process.ReadStdWString(s);
            }
            return null;
        }

        public static void ProbeTextAtAddress(IntPtr addr)
        {
            ProbeLogs.Clear();
            ProbeLogs.Add($"Probing 0x{addr:X}");
            for (int i = 0x300; i <= 0x550; i += 8)
            {
                if (Core.Process.ReadMemory<StdWString>(addr + i, out var stdW))
                {
                    var text = Core.Process.ReadStdWString(stdW);
                    if (!string.IsNullOrEmpty(text) && text.Length > 2) ProbeLogs.Add($"0x{i:X}: {text}");
                }
            }
        }
    }
}