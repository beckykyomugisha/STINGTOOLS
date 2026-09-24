// StingTools — the "â€”" family of text corruption.
//
// UTF-8 text decoded once as Windows-1252 turns "—" into "â€”" and "≤" into
// "â‰¤". 103 strings in STING_AEC_FILTERS.json carried it, five of them in
// filter NAMES — and a filter's name is its identity in a project: the
// factory finds an existing ParameterFilterElement by name. Correcting the
// name in the data alone would make every project that already holds
// "STING - … â‰¤ …" mint a second, correctly-named filter beside it.
//
// So both directions live here: Repair for the data, Garble for recognising
// what a project created from the corrupted data, so the factory can rename
// that filter instead of duplicating it. Revit-free and unit-tested.

using System;
using System.Text;

namespace StingTools.Core.Drawing
{
    public static class Utf8Mojibake
    {
        private static readonly Encoding Cp1252;

        static Utf8Mojibake()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Cp1252 = Encoding.GetEncoding(1252,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        }

        /// <summary>What correct text became after one UTF-8 → cp1252 mis-decode.
        /// Null when the text is pure ASCII (nothing could have been garbled) or
        /// cannot be represented that way.</summary>
        public static string Garble(string text)
        {
            if (string.IsNullOrEmpty(text) || IsAscii(text)) return null;
            try { return Cp1252.GetString(Encoding.UTF8.GetBytes(text)); }
            catch (DecoderFallbackException) { return null; }
        }

        /// <summary>Undo one mis-decode. Returns the input unchanged when it is
        /// not mojibake (pure ASCII, legitimate non-ASCII text, or bytes that do
        /// not form valid UTF-8) — so it is safe to run over any string.</summary>
        public static string Repair(string text)
        {
            if (string.IsNullOrEmpty(text) || IsAscii(text)) return text;
            try
            {
                var bytes = Cp1252.GetBytes(text);
                var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
                var fixedText = strict.GetString(bytes);
                return fixedText;
            }
            catch (EncoderFallbackException) { return text; }
            catch (ArgumentException) { return text; }
        }

        private static bool IsAscii(string s)
        {
            foreach (var c in s) if (c > 127) return false;
            return true;
        }
    }
}
