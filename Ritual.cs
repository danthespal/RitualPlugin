using GameOffsets.Natives;
using ImGuiNET;
using Newtonsoft.Json;
using OriathHub.Pricing;
using OriathHub.RemoteEnums;
using OriathHub.RemoteObjects.Components;
using OriathHub.RemoteObjects.States.InGameStateObjects;
using OriathHub.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace OriathHub.Plugins.Ritual
{
    public sealed class Ritual : OriathHub.Plugin.PluginBase
    {
        private RitualSettings _settings;
        private Dictionary<string, string> _omenMap = new();
        private FileInfo SettingsFile => new(Path.Combine(DllDirectory, "config", "settings.json"));

        public override string Name => "Ritual";
        public override string Author => "MrOne";
        public override string Description => "Pricer for Ritual Rewards";
        public override string Version => "0.3.242";

        public override void OnEnable(bool isGameOpened)
        {
            _settings = JsonHelper.CreateOrLoadJsonFile<RitualSettings>(SettingsFile);

            var omenPath = Path.Combine(DllDirectory, "config", "omens.json");
            if (File.Exists(omenPath))
            {
                var json = File.ReadAllText(omenPath);
                var list = JsonConvert.DeserializeObject<List<OmenEntry>>(json);
                _omenMap = list.ToDictionary(x => x.Id, x => x.Name);
            }
        }

        public override void SaveSettings() => JsonHelper.SaveToFile(_settings, SettingsFile);

        public override void DrawSettings()
        {
            if (_settings == null) return;
            bool changed = false;

            changed |= ImGui.Checkbox("Enable Overlay", ref _settings.EnableOverlay);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.SliderFloat("Font Scale", ref _settings.FontScale, 0.5f, 2.0f, "%.2f")) changed = true;

            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Display Prices In:");
            if (ImGui.RadioButton("Exalted Orbs", _settings.DisplayMode == PriceDisplayMode.Exalted)) { _settings.DisplayMode = PriceDisplayMode.Exalted; changed = true; }
            ImGui.SameLine();
            if (ImGui.RadioButton("Divine Orbs", _settings.DisplayMode == PriceDisplayMode.Divine)) { _settings.DisplayMode = PriceDisplayMode.Divine; changed = true; }

            if (changed) SaveSettings();
        }

        public override void DrawUI()
        {
            if (!_settings.EnableOverlay || Core.States.GameCurrentState != RemoteEnums.GameStateTypes.InGameState) return;

            var inGame = Core.States.InGameStateObject;
            var area = inGame.CurrentWorldInstance.AreaDetails;

            if (area.IsTown)
            {
                RitualUiParser.ClearCache("Town");
                return;
            }

            if (!inGame.GameUi.RightPanel.IsVisible)
            {
                RitualUiParser.ClearCache("Inventory not open");
                return;
            }

            var rewards = RitualUiParser.GetVisibleRewards();
            if (rewards.Count == 0) return;

            var drawList = ImGui.GetForegroundDrawList();

            var league = Core.Prices.League;

            double divineToExaltRate = Core.Prices.GetDivineToExaltedRate(league);

            foreach (var reward in rewards)
            {
                string artPath = "";
                IntPtr renderAddr = RitualUiParser.ResolveComponent(reward.EntityAddress, "RenderItem");
                if (renderAddr != IntPtr.Zero)
                {
                    var render = new RenderItem(renderAddr);
                    artPath = render.ResourcePath ?? "";
                }

                bool isOmen = reward.Path.Contains("/Omen", StringComparison.OrdinalIgnoreCase) || _omenMap.ContainsKey(reward.Path);
                Rarity calculatedRarity = isOmen ? Rarity.Normal : Rarity.Unique;
                var query = new PriceQuery(
                    Path: reward.Path,
                    Rarity: calculatedRarity,
                    ArtPath: artPath,
                    StackCount: 1
                );

                bool hasPrice = Core.Prices.TryGetPrice(in query, league, out var quote);

                if (hasPrice)
                {
                    string label;
                    double exValue = quote.ExaltedValue;

                    if (_settings.DisplayMode == PriceDisplayMode.Exalted)
                    {
                        label = $"{exValue:N1} Ex";
                    }
                    else
                    {
                        double divValue = divineToExaltRate > 0 ? exValue / divineToExaltRate : 0;
                        label = divValue >= 0.1 ? $"{divValue:N1} Div" : $"{divValue:N2} Div";
                    }

                    var baseTextSize = ImGui.CalcTextSize(label);
                    float targetFontSize = ImGui.GetFontSize() * _settings.FontScale;
                    var textSize = baseTextSize * _settings.FontScale;

                    Vector2 padding = new Vector2(4, 4);
                    var textPos = reward.Position + reward.Size - textSize - padding;

                    drawList.AddRectFilled(textPos - new Vector2(2, 1), textPos + textSize + new Vector2(2, 1), 0xEE000000, 2);

                    drawList.AddText(ImGui.GetFont(), targetFontSize, textPos, 0xFF00FFFF, label);
                }
            }
        }

        public override void OnDisable() { }
        private class OmenEntry { public string Id { get; set; } public string Name { get; set; } }
    }
}