// StingTools — Drawing Template Manager · T-5
//
// Revit-free resolver for a DrawingType.TitleBlockParams value template.
// TitleBlockParamApplier (write) and its Peek (read-only compare) both route
// through Resolve() so there is exactly one definition of what a template
// means.
//
// Grammar
//   ${Name}        ProjectInformation parameter, via the caller's lookup.
//                  The lookup returns null when the parameter does not
//                  exist (unbound) and "" when it exists but is empty.
//   {key}          caller token dict (DrawingTokenContext).
//   {key:Dn}       same, zero-padded to n digits when the value is an int.
//   {{  }}         a literal "{" / "}". The only way to put a brace in a
//                  title-block cell on purpose.
//
// Why a template can be UNRESOLVED
//   Before T-5 an unknown {token} was copied through literally and an
//   unbound ${Param} became "". The applier then wrote either result — so
//   Migrate (which passed no tokens at all) stamped "A-{lvl}-{seq:D3}" onto
//   title blocks, and every profile's ${PRJ_ORG_CLIENT_NAME} (the bound
//   parameter is PRJ_ORG_CLIENT_NAME_TXT) blanked the Client Name cell.
//   A brace in an issued title block and a silently blanked cell are both
//   wrong, so neither is written any more: Resolve() reports what it could
//   not resolve and the applier leaves that cell alone and says so.
//
//   A token that IS supplied but is empty resolves to "" and is written —
//   that is a caller's deliberate blank (DrawingTokenContext's rule), and it
//   is listed in EmptyTokens so it can still be reported.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace StingTools.Core.Drawing
{
    public sealed class TitleBlockTemplateResult
    {
        /// <summary>Resolved text. When <see cref="IsResolved"/> is false it
        /// still holds a best-effort rendering (unresolved pieces left
        /// literal) for display — it must not be written.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>{token} / {token:Dn} placeholders the dict could not
        /// supply, plus malformed / stray braces, as written in the template.</summary>
        public List<string> UnresolvedTokens { get; } = new List<string>();

        /// <summary>${Name} references whose parameter does not exist.</summary>
        public List<string> MissingProjectInfo { get; } = new List<string>();

        /// <summary>{token}s that were supplied but empty (written as "").</summary>
        public List<string> EmptyTokens { get; } = new List<string>();

        public bool IsResolved => UnresolvedTokens.Count == 0 && MissingProjectInfo.Count == 0;

        /// <summary>One-line operator explanation of why this is unresolved.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (UnresolvedTokens.Count > 0)
                parts.Add("token(s) " + string.Join(", ", UnresolvedTokens) + " not supplied");
            if (MissingProjectInfo.Count > 0)
                parts.Add("Project Information parameter(s) " + string.Join(", ", MissingProjectInfo) + " not bound");
            return string.Join("; ", parts);
        }
    }

    public static class TitleBlockTemplate
    {
        /// <summary>
        /// Names to try, in order, for a ${Name} reference. STING registry
        /// parameters carry a type suffix (PRJ_ORG_CLIENT_NAME_TXT), but every
        /// shipped profile and the DrawingType docs write the stem
        /// (${PRJ_ORG_CLIENT_NAME}). Exact name first so a project that did
        /// bind the stem keeps working.
        /// </summary>
        public static IEnumerable<string> ProjectInfoCandidates(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) yield break;
            yield return name;
            if (!name.EndsWith("_TXT", StringComparison.OrdinalIgnoreCase))
                yield return name + "_TXT";
        }

        public static TitleBlockTemplateResult Resolve(
            string template,
            Func<string, string> projectInfo,
            IDictionary<string, string> tokens)
        {
            var r = new TitleBlockTemplateResult();
            if (string.IsNullOrEmpty(template)) return r;

            var sb = new StringBuilder(template.Length);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];

                if (c == '{' && i + 1 < template.Length && template[i + 1] == '{')
                { sb.Append('{'); i += 2; continue; }
                if (c == '}' && i + 1 < template.Length && template[i + 1] == '}')
                { sb.Append('}'); i += 2; continue; }

                if (c == '$' && i + 1 < template.Length && template[i + 1] == '{')
                {
                    int close = template.IndexOf('}', i + 2);
                    if (close < 0) { Unresolved(r, sb, template.Substring(i)); break; }
                    string raw  = template.Substring(i, close - i + 1);
                    string name = template.Substring(i + 2, close - i - 2);
                    string val  = IsIdent(name) ? LookupProjectInfo(projectInfo, name) : null;
                    if (val == null)
                    {
                        if (!r.MissingProjectInfo.Contains(name)) r.MissingProjectInfo.Add(name);
                        sb.Append(raw);
                    }
                    else sb.Append(val);
                    i = close + 1;
                    continue;
                }

                if (c == '{')
                {
                    int close = template.IndexOf('}', i + 1);
                    if (close < 0) { Unresolved(r, sb, template.Substring(i)); break; }
                    string raw   = template.Substring(i, close - i + 1);
                    string inner = template.Substring(i + 1, close - i - 1);
                    if (TryParseToken(inner, out var key, out var width)
                        && tokens != null && tokens.TryGetValue(key, out var val) && val != null)
                    {
                        if (val.Length == 0 && !r.EmptyTokens.Contains(key)) r.EmptyTokens.Add(key);
                        if (width > 0 && int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iv))
                            val = iv.ToString("D" + width, CultureInfo.InvariantCulture);
                        sb.Append(val);
                    }
                    else Unresolved(r, sb, raw);
                    i = close + 1;
                    continue;
                }

                if (c == '}') { Unresolved(r, sb, "}"); i++; continue; }

                sb.Append(c);
                i++;
            }
            r.Text = sb.ToString();
            return r;
        }

        private static string LookupProjectInfo(Func<string, string> projectInfo, string name)
        {
            if (projectInfo == null) return null;
            foreach (var candidate in ProjectInfoCandidates(name))
            {
                var v = projectInfo(candidate);
                if (v != null) return v;
            }
            return null;
        }

        private static void Unresolved(TitleBlockTemplateResult r, StringBuilder sb, string raw)
        {
            if (!r.UnresolvedTokens.Contains(raw)) r.UnresolvedTokens.Add(raw);
            sb.Append(raw);
        }

        /// <summary>"key" or "key:Dn". Anything else is malformed.</summary>
        private static bool TryParseToken(string inner, out string key, out int width)
        {
            key = null; width = -1;
            if (string.IsNullOrEmpty(inner)) return false;
            int colon = inner.IndexOf(':');
            if (colon < 0) { key = inner; return IsIdent(key); }
            key = inner.Substring(0, colon);
            var fmt = inner.Substring(colon + 1);
            if (!IsIdent(key) || fmt.Length < 2 || (fmt[0] != 'D' && fmt[0] != 'd')) return false;
            return int.TryParse(fmt.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out width)
                   && width > 0 && width <= 12;
        }

        private static bool IsIdent(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var ch in s)
                if (!(char.IsLetterOrDigit(ch) || ch == '_')) return false;
            return true;
        }
    }

    /// <summary>
    /// Which token values an EXISTING sheet can prove, from its stamps.
    /// A null field means "not known" — the caller must omit that token so
    /// the title-block cell is left alone, never seed it with "" (which
    /// would overwrite a good value with a blank).
    /// </summary>
    public sealed class ExistingSheetTokens
    {
        public string Level { get; private set; }
        public string Mark  { get; private set; }
        public int?   Seq   { get; private set; }
        /// <summary>Where each value came from, for the operator report.</summary>
        public List<string> Sources { get; } = new List<string>();

        /// <param name="contextStamp">STING_SHEET_CONTEXT_TXT (null = unbound).</param>
        /// <param name="levelStamp">PRJ_SHEET_LEVEL_TXT (K-12 segment stamp).</param>
        /// <param name="seqStamp">PRJ_SHEET_SEQUENCE_INT; &lt;= 0 means unset.</param>
        /// <param name="seqFromNumber">Trailing digit run of the sheet number.</param>
        public static ExistingSheetTokens Resolve(
            string contextStamp, string levelStamp, int? seqStamp, int? seqFromNumber)
        {
            var t = new ExistingSheetTokens();
            var ctx = SheetProductionContext.Parse(contextStamp);

            if (ctx != null && !string.IsNullOrEmpty(ctx.Level))
            { t.Level = ctx.Level; t.Sources.Add("lvl<-context"); }
            else if (!string.IsNullOrWhiteSpace(levelStamp) && levelStamp.IndexOf('{') < 0)
            { t.Level = levelStamp; t.Sources.Add("lvl<-PRJ_SHEET_LEVEL_TXT"); }

            // The producer feeds ctx.Tag to BOTH {mark} and {spool}. An empty
            // tag in a parsed context is a real, known blank.
            if (ctx != null) { t.Mark = ctx.Tag; t.Sources.Add("mark<-context"); }

            if (seqStamp.HasValue && seqStamp.Value > 0)
            { t.Seq = seqStamp.Value; t.Sources.Add("seq<-PRJ_SHEET_SEQUENCE_INT"); }
            else if (seqFromNumber.HasValue)
            { t.Seq = seqFromNumber.Value; t.Sources.Add("seq<-sheet number"); }

            return t;
        }
    }

    /// <summary>
    /// The production-context stamp DrawingProducer writes to
    /// STING_SHEET_CONTEXT_TXT: "levelName::roomId::tag[::scopeBoxName]".
    /// Parsing it back lets a later pass (Heal, Migrate, drift) rebuild the
    /// same {lvl} / {mark} / {spool} the producer used, instead of guessing.
    /// </summary>
    public sealed class SheetProductionContext
    {
        public string Level    { get; private set; } = string.Empty;
        public string RoomId   { get; private set; } = string.Empty;
        public string Tag      { get; private set; } = string.Empty;
        public string ScopeBox { get; private set; } = string.Empty;

        /// <summary>Null for null/blank or a string that is not in the
        /// producer's format (fewer than three segments).</summary>
        public static SheetProductionContext Parse(string stamp)
        {
            if (string.IsNullOrWhiteSpace(stamp)) return null;
            var parts = stamp.Split(new[] { "::" }, StringSplitOptions.None);
            if (parts.Length < 3) return null;
            return new SheetProductionContext
            {
                Level    = parts[0],
                RoomId   = parts[1],
                Tag      = parts[2],
                // A scope-box name could itself contain "::"; keep the rest whole.
                ScopeBox = parts.Length > 3 ? string.Join("::", parts, 3, parts.Length - 3) : string.Empty,
            };
        }
    }
}
