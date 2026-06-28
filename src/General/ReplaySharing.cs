namespace ReplayTimerMod
{
    /// <summary>
    /// Pure helpers for the share-by-pointer flow. No Unity or network
    /// dependencies, so this is trivially testable in isolation.
    ///
    /// A share is a minimal base62 code (1–11 chars; the server emits the plain
    /// base62 of a counter, so early codes are a single character). The text we
    /// put on the clipboard is the bare code and nothing else.
    ///
    /// On paste we accept either the bare code or a full http(s) URL containing
    /// it. We deliberately do NOT scan arbitrary text for "/r/" or "code=",
    /// because a base64 replay blob can legitimately contain '/', 'r', and those
    /// letters — scanning would risk slicing a blob into something that looks
    /// like a code. Instead: a short all-base62 token is a code; anything that
    /// starts with http(s):// is parsed as a link; everything else (including
    /// every replay blob, which is far longer than a code) falls through to the
    /// normal blob importer.
    /// </summary>
    internal static class ReplaySharing
    {
        // A whole bare code is short base62 (1-12 chars). base62 of a bigint is
        // at most 11 chars; 12 gives headroom. A replay blob is always far
        // longer, so it can never match and be mistaken for a code.
        //
        // This is a hand-rolled char check rather than a Regex on purpose: a
        // static `new Regex(..., RegexOptions.Compiled)` initializer threw a
        // TypeInitializationException on the net35 / old-Unity-Mono build
        // (RegexOptions.Compiled needs Reflection.Emit), which took the whole
        // type down and broke every share/copy action on Hollow Knight 1.2.2.1.
        private const int MaxCodeLen = 12;

        private static bool IsBareCode(string s)
        {
            if (s.Length == 0 || s.Length > MaxCodeLen) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= '0' && c <= '9')
                       || (c >= 'A' && c <= 'Z')
                       || (c >= 'a' && c <= 'z');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// If <paramref name="text"/> is a share code (bare, or inside an
        /// http(s) URL), extracts it. Returns false for replay blobs and junk.
        /// </summary>
        public static bool TryExtractCode(string? text, out string code)
        {
            code = "";
            if (string.IsNullOrEmpty(text)) return false;

            string s = text!.Trim();

            // 1. Bare code — the whole token is short base62.
            if (IsBareCode(s))
            {
                code = s;
                return true;
            }

            // 2. Real URL — and only a real URL — may carry a code.
            if (s.StartsWith("http://") || s.StartsWith("https://"))
            {
                string path = s;
                string query = "";
                int q = path.IndexOfAny(new[] { '?', '#' });
                if (q >= 0)
                {
                    query = path.Substring(q + 1);
                    path = path.Substring(0, q);
                }

                // ?code=XXXX wins if present.
                int cIdx = query.IndexOf("code=");
                if (cIdx >= 0)
                {
                    string v = query.Substring(cIdx + 5);
                    int amp = v.IndexOf('&');
                    if (amp >= 0) v = v.Substring(0, amp);
                    if (IsBareCode(v)) { code = v; return true; }
                }

                // Otherwise the last non-empty path segment (e.g. /r/CODE).
                path = path.TrimEnd('/');
                int slash = path.LastIndexOf('/');
                string seg = slash >= 0 ? path.Substring(slash + 1) : path;
                if (IsBareCode(seg)) { code = seg; return true; }
            }

            return false;
        }

        /// <summary>
        /// The clipboard text for a share: the bare code, nothing wrapped around
        /// it. (The recipient pastes it straight back; the paste handler also
        /// accepts a full URL if someone shares one, but we never add one.)
        /// </summary>
        public static string BuildShareText(string code) => code;
    }
}