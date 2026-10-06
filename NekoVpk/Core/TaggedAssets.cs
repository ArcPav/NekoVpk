using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using DotNet.Globbing;
using SteamDatabase.ValvePak;
using Avalonia.Controls.Converters;

namespace NekoVpk.Core
{
    public static class TaggedAssets
    {
        public static List<AssetTagProperty> Tags { get; } = [];

        private static readonly object _tagLock = new();

        public static void Load()
        {
            if (Tags.Count > 0) return;

            try
            {
                LoadCore();
            }
            catch (Exception ex)
            {
                App.Logger.Error(ex, "TaggedAssets.jsonc 加载失败");
                lock (_tagLock) { Tags.Clear(); }
            }

            if (Tags.Count == 0)
            {
                App.Logger.Warn("TaggedAssets.jsonc 不可用,改用内置的 Survivor 标签");
                LoadBuiltInSurvivors();
            }
        }

        private static void AddBuiltInSurvivor(string name, string color, string[]? mutex, string[] files)
        {
            var tag = new AssetTagProperty(name, [.. files.Select(f => Glob.Parse(f.ToLower()))], color, ["Survivor"]);
            tag.Mutex = mutex;
            Tags.Add(tag);
        }

        private static void LoadBuiltInSurvivors()
        {
            AddBuiltInSurvivor("Bill", "Red", null,
            [
                "materials/vgui/s_panel_namvet.*",
                "materials/vgui/s_panel_namvet_incap.*",
                "materials/vgui/select_bill.*",
                "models/survivors/survivor_namvet.*",
                "models/weapons/arms/v_arms_bill.*",
                "models/survivors/namvet/namvet_deathpose.*",
                "models/survivors/bill*.*",
                "models/weapons/arms/bill*.*",
                "models/survivors/namvet/bill*.*"
            ]);

            AddBuiltInSurvivor("Coach", "Pink", null,
            [
                "materials/vgui/s_panel_coach.*",
                "materials/vgui/s_panel_coach_incap.*",
                "materials/vgui/s_panel_lobby_coach.*",
                "models/survivors/survivor_coach.*",
                "models/weapons/arms/v_arms_coach_new.*",
                "models/survivors/coach*.*",
                "models/weapons/arms/coach*.*"
            ]);

            AddBuiltInSurvivor("Ellis", "Purple", null,
            [
                "materials/vgui/s_panel_mechanic.*",
                "materials/vgui/s_panel_mechanic_incap.*",
                "materials/vgui/s_panel_lobby_mechanic.*",
                "models/survivors/survivor_mechanic.*",
                "models/weapons/arms/v_arms_mechanic_new.*",
                "models/survivors/ellis*.*",
                "models/weapons/arms/ellis*.*"
            ]);

            AddBuiltInSurvivor("Francis", "Violet", null,
            [
                "materials/vgui/s_panel_biker.*",
                "materials/vgui/s_panel_biker_incap.*",
                "materials/vgui/select_francis.*",
                "models/survivors/survivor_biker.*",
                "models/weapons/arms/v_arms_francis.*",
                "models/survivors/survivor_biker_light.*",
                "models/survivors/francis*.*",
                "models/weapons/arms/francis*.*"
            ]);

            AddBuiltInSurvivor("Louis", "Indigo", null,
            [
                "materials/vgui/s_panel_manager.*",
                "materials/vgui/s_panel_manager_incap.*",
                "materials/vgui/select_louis.*",
                "models/survivors/survivor_manager.*",
                "models/weapons/arms/v_arms_louis.*",
                "models/survivors/louis*.*",
                "models/weapons/arms/louis*.*"
            ]);

            AddBuiltInSurvivor("Nick", "Blue", null,
            [
                "materials/vgui/s_panel_gambler.*",
                "materials/vgui/s_panel_gambler_incap.*",
                "materials/vgui/s_panel_lobby_gambler.*",
                "models/survivors/survivor_gambler.*",
                "models/weapons/arms/v_arms_gambler_new.*",
                "models/survivors/nick*.*",
                "models/weapons/arms/nick*.*"
            ]);

            AddBuiltInSurvivor("Rochelle", "LightBlue", null,
            [
                "materials/vgui/s_panel_producer.*",
                "materials/vgui/s_panel_producer_incap.*",
                "materials/vgui/s_panel_lobby_producer.*",
                "models/survivors/survivor_producer.*",
                "models/weapons/arms/v_arms_producer_new.*",
                "models/survivors/rochelle*.*",
                "models/weapons/arms/rochelle*.*"
            ]);

            AddBuiltInSurvivor("Zoey", "Cyan", null,
            [
                "materials/vgui/s_panel_teenangst.*",
                "materials/vgui/s_panel_teenangst_incap.*",
                "materials/vgui/select_zoey.*",
                "models/survivors/survivor_teenangst.*",
                "models/weapons/arms/v_arms_zoey.*",
                "models/survivors/survivor_teenangst_light.*",
                "models/survivors/zoey*.*",
                "models/weapons/arms/zoey*.*"
            ]);

            AddBuiltInSurvivor("L4N-Survivor", "Grey", ["All"],
            [
                "models/l4n/s/*/*.*"
            ]);
        }

        public static AssetTag GetOrAddVersionTag(long version)
        {
            string glob = $"VPK-Version-{version}";
            lock (_tagLock)
            {
                var existing = GetAssetTag(glob, false);
                if (existing is not null) return existing;

                Tags.Add(new AssetTagProperty("0x" + Convert.ToString(version, 16).ToUpper(), [Glob.Parse(glob)]));
                return new AssetTag(Tags.Count - 1, false);
            }
        }

        private static void LoadCore()
        {
            if (Tags.Count > 0) return;
            Tags.Clear();

            string path = Path.Combine(AppContext.BaseDirectory, "TaggedAssets.jsonc");
            FileInfo file = new(File.Exists(path) ? path : "TaggedAssets.jsonc");
            if (!file.Exists) return;

            JObject? deserialized = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(file.FullName));
            if (deserialized == null || !deserialized.HasValues) return;

            foreach (var kv in deserialized)
            {
                if (kv.Value is JObject obj)
                {
                    JToken? token = obj["files"];
                    List<Glob> globs = [];
                    if (token is JArray array)
                    {
                        foreach (var glob in array)
                        {
                            try
                            {
                                globs.Add(Glob.Parse(glob.ToString().ToLower()));
                            }
                            catch (Exception ex)
                            {
                                App.Logger.Warn(ex, $"TaggedAssets.jsonc: 无效的 glob \"{glob}\" (标签 {kv.Key}),已跳过");
                            }
                        }
                    }

                    if (globs.Count == 0) continue;
                    Tags.Add(new(kv.Key, [.. globs]));

                    token = obj["color"];
                    if (token is JValue color)
                    {
                        Tags.Last().Color = color.ToString();
                    }

                    token = obj["type"];
                    if (token is JArray type)
                    {
                        string[] typeVal = new string[type.Count];
                        for (int i = 0; i < typeVal.Length; ++i)
                        {
                            typeVal[i] = type[i].ToString();
                        }
                        Tags.Last().Type = typeVal;
                    }

                    token = obj["mutex"];
                    if (token is JArray mutex)
                    {
                        string[] mutexVal = new string[mutex.Count];
                        for (int i = 0; i < mutexVal.Length; ++i)
                        {
                            mutexVal[i] = mutex[i].ToString();
                        }
                        Tags.Last().Mutex = mutexVal;
                    }
                }

            }
            return;
        }
        
        public static AssetTag? GetAssetTag(string path, bool isHidden)
        {
            for (int i = 0; i < Tags.Count; ++i)
            {
                if (Tags[i].IsMatch(path))
                {
                    return new (i, isHidden);
                }
            }

            return null;
        } 
    }
}
