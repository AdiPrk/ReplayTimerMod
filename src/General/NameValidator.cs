using System;
using System.Text;

namespace ReplayTimerMod
{
    /// <summary>
    /// Result of a display-name check. A plain struct (no tuples, no records,
    /// no init-only members) so it compiles on net35 / old Unity Mono as well
    /// as net472 and netstandard2.1.
    /// </summary>
    internal struct NameCheck
    {
        public bool Valid;
        public string Error;
        /// <summary>The exact value that should be sent to / stored by the server.</summary>
        public string Canonical;

        public NameCheck(bool valid, string error, string canonical)
        {
            Valid = valid;
            Error = error;
            Canonical = canonical;
        }
    }

    /// <summary>
    /// Validates display names. Rules (kept identical to the server-side
    /// validator in supabase/functions/_shared/validate.ts — change them
    /// together):
    ///
    ///   - 3–16 characters
    ///   - ASCII letters, digits, spaces, hyphens, underscores only
    ///   - Must contain at least one letter
    ///   - No leading/trailing whitespace
    ///   - No consecutive spaces
    ///   - No profanity (with leetspeak + separator de-obfuscation)
    ///
    /// Restricting to ASCII is deliberate: it eliminates an entire class of
    /// abuse (homoglyph/mixed-script impersonation, right-to-left overrides,
    /// zero-width / combining characters, fullwidth look-alikes) by reducing
    /// it to a single "non-ASCII → reject" rule, and lets the profanity filter
    /// reason about a small, predictable alphabet.
    ///
    /// This is the CLIENT check (instant UX). It is NOT a security boundary —
    /// the edge function re-validates authoritatively before any DB write, and
    /// the database enforces structural rules as a last line of defense.
    ///
    /// Avoids tuples / records / init-only setters / String.Normalize / Span
    /// so it builds and runs on every target including HK 1221 (net35).
    /// </summary>
    internal static class NameValidator
    {
        public const int MinLength = 3;
        public const int MaxLength = 16;

        /// <summary>
        /// Validates <paramref name="name"/>. On success, <c>Canonical</c> holds
        /// the exact string to send to the server.
        /// </summary>
        public static NameCheck Validate(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return Fail("Name cannot be empty.", "");

            // Canonical form is the trimmed input. We reject (rather than
            // silently rewrite) anything whose canonical form differs, so the
            // client and server always agree on the stored value.
            string canonical = name!.Trim();

            if (canonical.Length == 0)
                return Fail("Name cannot be empty.", "");
            if (canonical != name)
                return Fail("Name cannot start or end with spaces.", canonical);
            if (canonical.Length < MinLength)
                return Fail("Name must be at least " + MinLength + " characters.", canonical);
            if (canonical.Length > MaxLength)
                return Fail("Name cannot exceed " + MaxLength + " characters.", canonical);

            bool hasLetter = false;
            bool prevSpace = false;

            for (int i = 0; i < canonical.Length; i++)
            {
                char c = canonical[i];

                if (IsAsciiLetter(c))
                {
                    hasLetter = true;
                    prevSpace = false;
                }
                else if (IsAsciiDigit(c) || c == '-' || c == '_')
                {
                    prevSpace = false;
                }
                else if (c == ' ')
                {
                    if (prevSpace)
                        return Fail("Name cannot have consecutive spaces.", canonical);
                    prevSpace = true;
                }
                else
                {
                    return Fail(
                        "Only basic letters, numbers, spaces, hyphens, and underscores are allowed.",
                        canonical);
                }
            }

            if (!hasLetter)
                return Fail("Name must contain at least one letter.", canonical);

            if (ContainsProfanity(canonical))
                return Fail("That name contains inappropriate language.", canonical);

            if (IsReserved(canonical))
                return Fail("That name is reserved.", canonical);

            return new NameCheck(true, "", canonical);
        }

        private static NameCheck Fail(string error, string canonical)
        {
            return new NameCheck(false, error, canonical);
        }

        // ── Character classes (ASCII-only, culture-independent) ────────────

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');
        }

        private static bool IsAsciiDigit(char c)
        {
            return c >= '0' && c <= '9';
        }

        // ── Profanity detection ────────────────────────────────────────────
        // Runs on the already-validated canonical name, which is guaranteed
        // ASCII by the loop above. We de-leet and strip separators so common
        // evasions ("f4ggot", "n-i-g-g-e-r", "f_u_c_k") still match.

        private static bool ContainsProfanity(string canonical)
        {
            string lower = DeLeet(canonical.ToLowerInvariant());

            // Slur pass: drop everything but letters, then substring match.
            // Slurs are blocked even when embedded.
            var lettersOnly = new StringBuilder(lower.Length);
            for (int i = 0; i < lower.Length; i++)
            {
                char c = lower[i];
                if (IsAsciiLetter(c))
                    lettersOnly.Append(c);
            }
            string letters = lettersOnly.ToString();
            for (int i = 0; i < AlwaysBlockSubstring.Length; i++)
            {
                if (IndexOfOrdinal(letters, AlwaysBlockSubstring[i]) >= 0)
                    return true;
            }

            return MatchesWholeWord(canonical, BlockAsWholeWord);
        }

        private static bool IsReserved(string canonical)
        {
            return MatchesWholeWord(canonical, ReservedWord);
        }

        /// <summary>
        /// True if any whole word in <paramref name="list"/> appears in the
        /// canonical name, after de-leeting and undoing separator obfuscation.
        /// </summary>
        private static bool MatchesWholeWord(string canonical, string[] list)
        {
            string lower = DeLeet(canonical.ToLowerInvariant());

            // Pass A: separators (- _) → space, preserving token boundaries.
            string wordSpaced = ReplaceSeparatorsWithSpace(lower);
            // Pass B: all separators removed, collapsing a spread-out word.
            string wordStripped = KeepAlphanumeric(lower);

            for (int i = 0; i < list.Length; i++)
            {
                string w = list[i];
                if (IsWholeWord(wordSpaced, w) || IsWholeWord(wordStripped, w))
                    return true;
            }
            return false;
        }

        private static int IndexOfOrdinal(string text, string value)
        {
            return text.IndexOf(value, StringComparison.Ordinal);
        }

        private static bool IsWholeWord(string text, string word)
        {
            int idx = 0;
            while (true)
            {
                idx = text.IndexOf(word, idx, StringComparison.Ordinal);
                if (idx < 0) return false;

                bool startOk = idx == 0 || !IsWordChar(text[idx - 1]);
                int end = idx + word.Length;
                bool endOk = end >= text.Length || !IsWordChar(text[end]);

                if (startOk && endOk) return true;
                idx++;
            }
        }

        private static bool IsWordChar(char c)
        {
            return IsAsciiLetter(c) || IsAsciiDigit(c);
        }

        private static string ReplaceSeparatorsWithSpace(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                sb.Append(c == '-' || c == '_' ? ' ' : c);
            }
            return sb.ToString();
        }

        private static string KeepAlphanumeric(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (IsAsciiLetter(c) || IsAsciiDigit(c))
                    sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Folds common leetspeak substitutions so "a$$" matches "ass",
        /// "f4g" matches "fag", etc. Applied to the profanity-check copy only.
        /// </summary>
        private static string DeLeet(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '@': sb.Append('a'); break;
                    case '4': sb.Append('a'); break;
                    case '3': sb.Append('e'); break;
                    case '1': sb.Append('i'); break;
                    case '!': sb.Append('i'); break;
                    case '0': sb.Append('o'); break;
                    case '5': sb.Append('s'); break;
                    case '$': sb.Append('s'); break;
                    case '7': sb.Append('t'); break;
                    case '+': sb.Append('t'); break;
                    case '8': sb.Append('b'); break;
                    case '9': sb.Append('g'); break;
                    default:  sb.Append(c);   break;
                }
            }
            return sb.ToString();
        }

        // ── Word lists ─────────────────────────────────────────────────────
        // KEEP IN SYNC with validate.ts (BLOCK_SUBSTRING / BLOCK_WORD).

        // Blocked as substrings (even embedded). Slurs/epithets only.
        private static readonly string[] AlwaysBlockSubstring = new[]
        {
            "nigger", "nigga",
            "faggot", "fagot",
            "retard",
            "tranny",
            "chink",
            "spic", "spick",
            "kike",
            "dyke",
            "coon",
            "gook",
            "wetback",
            "beaner",
            "raghead",
            "towelhead",
            "zipperhead",
        };

        // Blocked only as whole words (they appear inside legitimate words:
        // "assassin", "classic", "cockpit", "Dickson", "Scunthorpe", ...).
        private static readonly string[] BlockAsWholeWord = new[]
        {
            "fuck", "fucker", "fucked", "fucking", "fucks",
            "shit", "shitty", "shits",
            "ass", "asses", "asshole", "assholes",
            "bitch", "bitches",
            "cunt", "cunts",
            "dick", "dicks",
            "cock", "cocks",
            "piss", "pissed",
            "damn", "damned",
            "whore", "whores",
            "slut", "sluts",
            "bastard", "bastards",
            "wanker", "wankers",
            "twat", "twats",
            "tits",
            "nazi", "nazis",
            "porn",
            "penis", "vagina",
            "fag", "fags",
            "heil",
            "hitler",
            "kkk",
        };

        // Reserved identities — blocked as whole words to prevent impersonation
        // of staff / the system. KEEP IN SYNC with validate.ts (RESERVED_WORD).
        private static readonly string[] ReservedWord = new[]
        {
            "admin", "administrator", "moderator", "anthropic",
            "official", "staff", "system",
        };
    }
}