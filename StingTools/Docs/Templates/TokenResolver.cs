// TokenResolver.cs — template engine v1.1 (S05).
//
// Thin resolver used by MiniWordAdapter's pre-processor: discovers every
// {{token}} in a template string, resolves one at a time against TokenContext,
// and recognises the small set of control-flow constructs we flatten before
// handing the template off to MiniWord (which itself supports one level of
// loops natively; we flatten nested loops ourselves).

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Planscape.Docs.Templates
{
    public static class TokenResolver
    {
        private static readonly Regex TokenRx = new Regex(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);

        /// <summary>Returns every distinct raw token (trimmed inner text) found in <paramref name="text"/>.</summary>
        public static List<string> FindAllTokens(string text)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text)) return new List<string>();
            foreach (Match m in TokenRx.Matches(text))
            {
                string inner = m.Groups[1].Value.Trim();
                if (inner.Length > 0) seen.Add(inner);
            }
            return new List<string>(seen);
        }

        /// <summary>Resolves a single token against the context (dotted path).
        /// Returns <c>&lt;TOKEN_NOT_FOUND:path&gt;</c> when the lookup fails.</summary>
        public static string Resolve(string token, TokenContext ctx)
        {
            if (string.IsNullOrEmpty(token) || ctx == null) return "";

            // Strip known MiniWord decorators that shouldn't be resolved here.
            if (IsLoopStart(token) || IsLoopEnd(token)) return "";
            if (IsIfStart(token) || IsIfEnd(token))     return "";
            if (token.StartsWith("image:", StringComparison.OrdinalIgnoreCase)) return "";
            if (token.StartsWith("link:",  StringComparison.OrdinalIgnoreCase)) return "";

            var flat = ctx.AsDictionary();
            if (flat.TryGetValue(token, out object direct) && direct != null) return direct.ToString() ?? "";

            // Walk dotted path manually for nested Dictionary/List structures.
            string[] parts = token.Split('.');
            object cur = flat.TryGetValue(parts[0], out object root) ? root : null;
            for (int i = 1; i < parts.Length && cur != null; i++)
            {
                if (cur is IDictionary<string, object> d && d.TryGetValue(parts[i], out object next))
                    cur = next;
                else
                    return $"<TOKEN_NOT_FOUND:{token}>";
            }
            return cur?.ToString() ?? $"<TOKEN_NOT_FOUND:{token}>";
        }

        // ── Control-flow token classification ───────────────────────────────

        public static bool IsLoopStart(string s)
            => !string.IsNullOrEmpty(s) && (s.StartsWith("#foreach ", StringComparison.OrdinalIgnoreCase)
                                          || s.StartsWith("#each ",    StringComparison.OrdinalIgnoreCase)
                                          || s.StartsWith("#loop ",    StringComparison.OrdinalIgnoreCase));

        public static bool IsLoopEnd(string s)
            => !string.IsNullOrEmpty(s) && (s.Equals("/foreach", StringComparison.OrdinalIgnoreCase)
                                         || s.Equals("/each",    StringComparison.OrdinalIgnoreCase)
                                         || s.Equals("/loop",    StringComparison.OrdinalIgnoreCase)
                                         || s.Equals("endforeach",StringComparison.OrdinalIgnoreCase));

        public static bool IsIfStart(string s)
            => !string.IsNullOrEmpty(s) && s.StartsWith("#if ", StringComparison.OrdinalIgnoreCase);

        public static bool IsIfEnd(string s)
            => !string.IsNullOrEmpty(s) && (s.Equals("/if", StringComparison.OrdinalIgnoreCase)
                                         || s.Equals("endif", StringComparison.OrdinalIgnoreCase));

        /// <summary>Extracts the loop variable from "#foreach items" → "items".</summary>
        public static string LoopName(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            foreach (var prefix in new[] { "#foreach ", "#each ", "#loop " })
            {
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return s.Substring(prefix.Length).Trim();
            }
            return null;
        }

        /// <summary>Extracts the condition expression from "#if doc.supersedes" → "doc.supersedes".</summary>
        public static string IfExpression(string s)
        {
            if (string.IsNullOrEmpty(s) || !s.StartsWith("#if ", StringComparison.OrdinalIgnoreCase))
                return null;
            return s.Substring(4).Trim();
        }

        /// <summary>True if an #if expression should render its block.</summary>
        public static bool EvaluateIf(string expression, TokenContext ctx)
        {
            if (string.IsNullOrEmpty(expression)) return false;
            string value = Resolve(expression, ctx);
            if (string.IsNullOrEmpty(value))         return false;
            if (value.StartsWith("<TOKEN_NOT_FOUND")) return false;
            if (string.Equals(value, "0", StringComparison.Ordinal))    return false;
            if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }
    }
}
