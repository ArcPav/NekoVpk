using Avalonia.Controls.Chrome;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ValveKeyValue;
using UtfUnknown;

namespace NekoVpk.Core
{
    public class AddonInfo
    {
        [KVProperty("AddonAuthor")]
        public string? Author { get; set; }

        [KVProperty("AddonTitle")]
        public string? Title { get; set; }

        [KVProperty("AddonVersion")]
        public string? Version { get; set; }

        [KVProperty("AddonDescription")]
        public string? Description { get; set; }

        [KVProperty("addonURL0")]
        public string? Url0 { get; set; }

        [KVProperty("nekovpk_7zname")]
        public string? NekoVpk7zName { get; set; }

        [KVProperty("nekovpk_active7z")]
        public string? NekoVpkActive7z { get; set; }

        public static AddonInfo Load(byte[] data)
        {
            var charset = CharsetDetector.DetectFromBytes(data);
            KVSerializerOptions options = new()
            {
                HasEscapeSequences = true,
                Encoding = charset.Detected?.Encoding ?? Encoding.UTF8
            };

            var kvs = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

            return kvs.Deserialize<AddonInfo>(new MemoryStream(data), options);
        }

        public static byte[] UpdateActive7z(byte[] data, string newActive)
        {
            var charset = CharsetDetector.DetectFromBytes(data);
            var encoding = charset.Detected?.Encoding ?? Encoding.UTF8;
            string text = encoding.GetString(data);
            
            text = Regex.Replace(text, @"(?i)[ \t]*""nekovpk_active7z""[ \t]+""[^""]*""[ \t]*\r?\n?", "");
            
            int lastBrace = text.LastIndexOf('}');
            if (lastBrace != -1)
            {
                string insert = $"\n\t\"nekovpk_active7z\"\t\"{newActive}\"\n";
                text = text.Insert(lastBrace, insert);
            }
            else
            {
                text += $"\n\"nekovpk_active7z\"\t\"{newActive}\"\n";
            }
            return encoding.GetBytes(text);
        }
    }
}