using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UtfUnknown;
using ValveKeyValue;
using System.Globalization;


namespace NekoVpk.Core
{
    public class AddonList
    {
        KVDocument KeyValue;

        readonly KVSerializerOptions SerializerOptions;
        KVCollectionValue Collection { get => (KVCollectionValue)KeyValue.Value; }

        public AddonList()
        {
            SerializerOptions = new KVSerializerOptions
            {
                HasEscapeSequences = false,
            };
            KeyValue = new("AddonList", new KVCollectionValue());
        }

        public void Load(string gameDir)
        {
            FileInfo file = GetFileInfo(gameDir);
            if (file.Exists)
            {
                byte[] data = File.ReadAllBytes(file.FullName);
                if (data.Length == 0) return;

                SerializerOptions.Encoding = DetectEncoding(data);

                var kvs = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

                using var stream = new MemoryStream(data);
                var deserialized = kvs.Deserialize(stream, SerializerOptions);

                if (deserialized != null 
                    && deserialized.Name.Equals("AddonList", StringComparison.OrdinalIgnoreCase) 
                    && deserialized.Value is KVCollectionValue)
                {
                    KeyValue = deserialized;
                }

            }
        }

        static Encoding DetectEncoding(byte[] data)
        {
            bool isUtf16 = data.Length >= 2 &&
                ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF));
            if (!isUtf16)
            {
                bool hasBom = data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF;
                try
                {
                    new UTF8Encoding(false, true).GetString(data);
                    return new UTF8Encoding(hasBom);
                }
                catch (DecoderFallbackException)
                {
                }
            }

            var detected = CharsetDetector.DetectFromBytes(data).Detected;
            var encoding = detected?.Encoding;

            if (encoding != null)
            {
                if (encoding.CodePage == 20127) return new UTF8Encoding(false);
                if (encoding.CodePage == 65001) return new UTF8Encoding(detected!.HasBOM);
                return encoding;
            }

            try
            {
                new UTF8Encoding(false, true).GetString(data);
                return new UTF8Encoding(false);
            }
            catch (DecoderFallbackException)
            {
            }

            try
            {
                int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
                if (codePage > 0 && codePage != 65001)
                {
                    var ansi = CodePagesEncodingProvider.Instance.GetEncoding(
                        codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                    if (ansi != null) return ansi;
                }
            }
            catch (Exception)
            {
            }

            throw new InvalidDataException("Cannot determine the encoding of addonlist.txt.");
        }

        public void SetEnable(string fileName, bool enable = true)
        {
            Collection.Set(fileName, enable ? 1:0);
        }

        public bool? IsEnabled(string fileName)
        {
            KVValue? val = Collection[fileName];
            if (val != null)
            {
                try
                {
                    return val.ToInt32(CultureInfo.CurrentCulture) == 1;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }

        public void Save(string gameDir)
        {
            var file = GetFileInfo(gameDir);
            var kvs = KVSerializer.Create(KVSerializationFormat.KeyValues1Text);

            string tmpPath = file.FullName + ".nekotmp";
            try
            {
                using (var writeStream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    kvs.Serialize(writeStream, KeyValue, SerializerOptions);
                }

                if (file.Exists)
                    File.Replace(tmpPath, file.FullName, null);
                else
                    File.Move(tmpPath, file.FullName);
            }
            finally
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
        }

        protected static FileInfo GetFileInfo(string gameDir) => new(Path.Join(gameDir, "addonlist.txt"));

    }
}
