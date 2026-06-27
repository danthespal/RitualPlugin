using OriathHub.RemoteEnums;
using System.Collections.Generic;
using System.Numerics;

namespace OriathHub.Plugins.Ritual
{
    public enum PriceDisplayMode
    {
        Exalted,
        Divine
    }

    public sealed class RitualSettings
    {
        public bool EnableOverlay = true;
        public Dictionary<string, string> CustomNames { get; set; } = new();
        public PriceDisplayMode DisplayMode { get; set; } = PriceDisplayMode.Divine;
        public float FontScale = 1.0f;
    }
}