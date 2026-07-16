using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class NameValidatorTests
    {
        private static NameCheck Check(string? name) => NameValidator.Validate(name);

        // ── Structural rules ────────────────────────────────────────────────

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyOrWhitespace_IsRejected(string? name) =>
            Assert.False(Check(name).Valid);

        [Fact]
        public void LeadingOrTrailingSpaces_AreRejected_NotTrimmedSilently()
        {
            // Client and server agree on the stored value by rejecting rather
            // than rewriting; Canonical still carries the trimmed form.
            var check = Check(" Hornet");
            Assert.False(check.Valid);
            Assert.Equal("Hornet", check.Canonical);
        }

        [Theory]
        [InlineData("ab", false)]
        [InlineData("abc", true)]
        [InlineData("abcdefghijklmnop", true)]   // 16
        [InlineData("abcdefghijklmnopq", false)] // 17
        public void LengthBounds(string name, bool valid) =>
            Assert.Equal(valid, Check(name).Valid);

        [Theory]
        [InlineData("Hor net", true)]
        [InlineData("Hor  net", false)]
        [InlineData("a-b_c 1", true)]
        [InlineData("12345", false)]  // no letter
        [InlineData("___", false)]    // no letter
        [InlineData("a__", true)]
        [InlineData("Hörnet", false)] // non-ASCII
        [InlineData("Hornet!", false)]
        [InlineData("Hornet.", false)]
        public void CharacterRules(string name, bool valid) =>
            Assert.Equal(valid, Check(name).Valid);

        [Fact]
        public void ValidName_CanonicalIsInput()
        {
            var check = Check("Hollow Knight");
            Assert.True(check.Valid);
            Assert.Equal("Hollow Knight", check.Canonical);
            Assert.Equal("", check.Error);
        }

        // ── Profanity & reserved words ──────────────────────────────────────

        [Theory]
        [InlineData("fuck")]
        [InlineData("FuCkEr")]
        [InlineData("f-u-c-k")]      // separator obfuscation
        [InlineData("f_u_c_k")]
        [InlineData("d1ck")]         // leetspeak
        [InlineData("p0rn")]
        [InlineData("as5")]          // de-leet → "ass"
        [InlineData("sh1t hero")]
        [InlineData("nigger123")]    // slur: substring, embedded
        [InlineData("xX-n1gg3r-Xx")]
        [InlineData("kkk")]
        [InlineData("k-k-k")]
        [InlineData("heil hitler")]
        public void Profanity_IsRejected(string name) =>
            Assert.False(Check(name).Valid);

        [Theory]
        [InlineData("assassin")]     // whole-word words don't match embedded
        [InlineData("Classic")]
        [InlineData("Scunthorpe")]
        [InlineData("Dickson")]
        [InlineData("kkkevin")]
        [InlineData("officially")]
        public void LegitimateWords_AreAllowed(string name) =>
            Assert.True(Check(name).Valid);

        [Theory]
        [InlineData("admin")]
        [InlineData("Administrator")]
        [InlineData("the_admin")]
        [InlineData("MODERATOR")]
        [InlineData("official")]
        [InlineData("staff")]
        [InlineData("system")]
        public void ReservedNames_AreRejected(string name) =>
            Assert.False(Check(name).Valid);

        [Theory]
        [InlineData("admins")]
        [InlineData("staffan")]
        public void ReservedWords_OnlyMatchWholeWords(string name) =>
            Assert.True(Check(name).Valid);

        // ── Shared corpus (parity with supabase validate.ts) ────────────────
        // The same corpus is run against the server validator by
        // supabase/functions/tests/validate_test.ts. If either side drifts,
        // one of the two suites fails.

        public static IEnumerable<object[]> CorpusCases()
        {
            string path = Path.Combine(RepoPaths.SharedDir, "name-cases.json");
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            foreach (var c in doc.RootElement.EnumerateArray())
            {
                yield return new object[]
                {
                    c.GetProperty("name").GetString()!,
                    c.GetProperty("client").GetBoolean(),
                };
            }
        }

        [Theory]
        [MemberData(nameof(CorpusCases))]
        public void SharedCorpus_ClientVerdictsMatch(string name, bool expectValid) =>
            Assert.Equal(expectValid, Check(name).Valid);
    }
}
