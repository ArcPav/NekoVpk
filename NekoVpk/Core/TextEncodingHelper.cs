using System;
using System.Globalization;
using System.Text;
using UtfUnknown;

namespace NekoVpk.Core;

public static class TextEncodingHelper
{
    static TextEncodingHelper()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    private static bool StartsWith(byte[] d, params byte[] prefix)
    {
        if (d.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++)
        {
            if (d[i] != prefix[i]) return false;
        }
        return true;
    }

    public static Encoding Detect(byte[] data, bool strict = false)
    {
        Encoding result;

        if (StartsWith(data, 0xEF, 0xBB, 0xBF))
        {
            result = new UTF8Encoding(false);
        }
        else if (StartsWith(data, 0xFF, 0xFE, 0x00, 0x00))
        {
            result = new UTF32Encoding(false, true);
        }
        else if (StartsWith(data, 0xFF, 0xFE))
        {
            result = new UnicodeEncoding(false, true);
        }
        else if (StartsWith(data, 0xFE, 0xFF))
        {
            result = new UnicodeEncoding(true, true);
        }
        else
        {
            result = DetectWithoutBom(data);
        }

        if (strict) result = MakeStrict(result);
        return result;
    }

    private static Encoding DetectWithoutBom(byte[] data)
    {
        try
        {
            new UTF8Encoding(false, true).GetString(data);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
        }

        Encoding? detected = null;
        try
        {
            detected = CharsetDetector.DetectFromBytes(data).Detected?.Encoding;
        }
        catch (Exception)
        {
        }

        if (detected != null && detected.CodePage != 20127)
        {
            return detected.CodePage == 65001 ? new UTF8Encoding(false) : detected;
        }

        try
        {
            int codePage = CultureInfo.CurrentCulture.TextInfo.ANSICodePage;
            if (codePage > 0 && codePage != 65001) return Encoding.GetEncoding(codePage);
        }
        catch (Exception)
        {
        }
        return Encoding.Latin1;
    }

    private static Encoding MakeStrict(Encoding encoding)
    {
        var clone = (Encoding)encoding.Clone();
        clone.EncoderFallback = EncoderFallback.ExceptionFallback;
        clone.DecoderFallback = DecoderFallback.ExceptionFallback;
        return clone;
    }
}
