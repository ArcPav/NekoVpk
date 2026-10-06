using System;
using System.Collections.Generic;
using System.Linq;

namespace NekoVpk.Core
{
    public static class WorkshopTagTaxonomy
    {
        public static readonly (string Category, string[] Tags)[] Categories =
        [
            ("Survivors", ["Bill", "Francis", "Louis", "Zoey", "Coach", "Ellis", "Nick", "Rochelle"]),
            ("Infected", ["Common Infected", "Special Infected", "Boomer", "Charger", "Hunter", "Jockey", "Smoker", "Spitter", "Tank", "Witch"]),
            ("Game Content", ["Campaign", "Campaigns", "Weapon", "Weapons", "Items", "Sounds", "Scripts", "UI", "Model", "Models", "Texture", "Textures"]),
            ("Game Modes", ["Co-op", "Versus", "Survival", "Realism", "Single Player", "Mutations"]),
            ("Weapons Detail", ["Melee", "Pistol", "Rifle", "Shotgun", "SMG", "Sniper", "Throwable"]),
            ("Items Detail", ["Adrenaline", "Defibrillator", "Medkit", "Pills"]),
        ];

        private static readonly Lazy<Dictionary<string, string>> _tagToCategory = new(BuildLookup);

        private static Dictionary<string, string> BuildLookup()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (category, tags) in Categories)
            {
                map[category] = category;
                foreach (var tag in tags)
                {
                    map[tag] = category;
                }
            }
            return map;
        }

        public static string ToShortDisplay(string? joinedTags)
        {
            if (string.IsNullOrWhiteSpace(joinedTags)) return string.Empty;

            var map = _tagToCategory.Value;
            var seen = new List<string>();

            foreach (var raw in joinedTags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (raw.Length == 0) continue;

                var label = map.TryGetValue(raw, out var category) ? category : raw;
                if (!seen.Contains(label, StringComparer.OrdinalIgnoreCase))
                {
                    seen.Add(label);
                }
            }

            return string.Join(", ", seen);
        }
    }
}
