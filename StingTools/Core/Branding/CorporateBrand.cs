// StingTools v4 MVP — Phase I.5 corporate branding.
//
// One source of truth for the company masthead, palette, fonts,
// page defaults and footer disclaimer applied to every STING-
// generated deliverable. Loaded from STING_CORPORATE_BRAND.json
// (with per-project override at <project>/_BIM_COORD/brand.json)
// and cached. Consumed by:
//
//   Templates (MiniWord + ClosedXML): resolve {{company_name}} /
//     {{company_short}} / {{palette.primary_hex}} / etc. via the
//     Brand.TokenBag() dictionary.
//   CSV / XML exporters: prepend two metadata rows via
//     Brand.StampCsvHeader(streamWriter).
//   BCF 2.1 comments: seed first CoordComment with Brand.Disclaimer.
//   COBie workbooks: cover sheet pulls logo + palette.
//
// Users override per-project by placing their own brand.json in
// _BIM_COORD/; the loader prefers the project copy when present.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Autodesk.Revit.DB;

namespace StingTools.Core.Branding
{
    public class BrandPalette
    {
        public string PrimaryHex   { get; set; } = "#E8912D";
        public string SecondaryHex { get; set; } = "#2D2D30";
        public string AccentHex    { get; set; } = "#4CAF50";
        public string TextBodyHex  { get; set; } = "#222222";
        public string TextMutedHex { get; set; } = "#666666";
        public string RuleHex      { get; set; } = "#CCCCCC";
        public string RagGreen     { get; set; } = "#2E7D32";
        public string RagAmber     { get; set; } = "#B7950B";
        public string RagRed       { get; set; } = "#A9322D";
    }

    public class BrandInfo
    {
        public string CompanyName    { get; set; } = "Planscape Limited";
        public string CompanyShort   { get; set; } = "PLNS";
        public string CompanyAddress { get; set; } = "";
        public string CompanyEmail   { get; set; } = "";
        public string CompanyWebsite { get; set; } = "";
        public string CompanyPhone   { get; set; } = "";
        public string CompanyTagline { get; set; } = "";
        public string LogoPrimary    { get; set; } = "";
        public string LogoMono       { get; set; } = "";
        public BrandPalette Palette  { get; set; } = new BrandPalette();
        public string FooterText     { get; set; } = "";
        public string Disclaimer     { get; set; } = "";
        public string CopyrightMask  { get; set; } = "";
    }

    public static class CorporateBrand
    {
        private static readonly object _lock = new object();
        private static BrandInfo _cache;
        private static string _cacheDocKey;

        /// <summary>
        /// Load the corporate brand for the given document. Project-
        /// override copy at _BIM_COORD/brand.json wins; fall back to
        /// Data/Templates/STING_CORPORATE_BRAND.json.
        /// </summary>
        public static BrandInfo For(Document doc)
        {
            string docKey = doc?.PathName ?? "";
            lock (_lock)
            {
                if (_cache != null && _cacheDocKey == docKey) return _cache;
                _cache       = Load(doc);
                _cacheDocKey = docKey;
                return _cache;
            }
        }

        public static void Invalidate()
        {
            lock (_lock) { _cache = null; _cacheDocKey = null; }
        }

        private static BrandInfo Load(Document doc)
        {
            var brand = new BrandInfo();
            try
            {
                string path = ResolvePath(doc);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    StingLog.Warn("CorporateBrand: manifest not found, using in-code defaults");
                    return brand;
                }
                var root = JObject.Parse(File.ReadAllText(path));
                var b = root["brand"];
                if (b != null)
                {
                    brand.CompanyName    = b.Value<string>("company_name")          ?? brand.CompanyName;
                    brand.CompanyShort   = b.Value<string>("company_short")         ?? brand.CompanyShort;
                    brand.CompanyAddress = b.Value<string>("company_address")       ?? "";
                    brand.CompanyEmail   = b.Value<string>("company_contact_email") ?? "";
                    brand.CompanyWebsite = b.Value<string>("company_website")       ?? "";
                    brand.CompanyPhone   = b.Value<string>("company_phone")         ?? "";
                    brand.CompanyTagline = b.Value<string>("company_tagline")       ?? "";
                    brand.LogoPrimary    = b.Value<string>("logo_primary_path")     ?? "";
                    brand.LogoMono       = b.Value<string>("logo_mono_path")        ?? "";
                }
                var p = root["palette"];
                if (p != null)
                {
                    brand.Palette.PrimaryHex   = p.Value<string>("primary_hex")   ?? brand.Palette.PrimaryHex;
                    brand.Palette.SecondaryHex = p.Value<string>("secondary_hex") ?? brand.Palette.SecondaryHex;
                    brand.Palette.AccentHex    = p.Value<string>("accent_hex")    ?? brand.Palette.AccentHex;
                    brand.Palette.TextBodyHex  = p.Value<string>("text_body_hex") ?? brand.Palette.TextBodyHex;
                    brand.Palette.TextMutedHex = p.Value<string>("text_muted_hex")?? brand.Palette.TextMutedHex;
                    brand.Palette.RuleHex      = p.Value<string>("rule_hex")      ?? brand.Palette.RuleHex;
                    brand.Palette.RagGreen     = p.Value<string>("rag_green")     ?? brand.Palette.RagGreen;
                    brand.Palette.RagAmber     = p.Value<string>("rag_amber")     ?? brand.Palette.RagAmber;
                    brand.Palette.RagRed       = p.Value<string>("rag_red")       ?? brand.Palette.RagRed;
                }
                var d = root["document_defaults"];
                if (d != null)
                {
                    brand.FooterText    = d.Value<string>("footer_text")    ?? "";
                    brand.Disclaimer    = d.Value<string>("disclaimer")     ?? "";
                    brand.CopyrightMask = d.Value<string>("copyright_mask") ?? "";
                }
                StingLog.Info($"CorporateBrand: loaded brand '{brand.CompanyName}' from {path}");
            }
            catch (Exception ex)
            { StingLog.Warn($"CorporateBrand.Load: {ex.Message}"); }
            return brand;
        }

        private static string ResolvePath(Document doc)
        {
            try
            {
                var projDir = Path.GetDirectoryName(doc?.PathName ?? "");
                if (!string.IsNullOrEmpty(projDir))
                {
                    var over = Path.Combine(projDir, "_BIM_COORD", "brand.json");
                    if (File.Exists(over)) return over;
                }
            }
            catch { }
            return Core.StingToolsApp.FindDataFile("Templates/STING_CORPORATE_BRAND.json")
                ?? Core.StingToolsApp.FindDataFile("STING_CORPORATE_BRAND.json");
        }
    }
}
