using ReplayTimerMod;
using Xunit;

namespace ReplayTimerMod.Tests
{
    public class JsonTextTests
    {
        [Theory]
        [InlineData("plain", "plain")]
        [InlineData("with \"quotes\"", "with \\\"quotes\\\"")]
        [InlineData("back\\slash", "back\\\\slash")]
        [InlineData("line\nbreak\r\ttab", "line\\nbreak\\r\\ttab")]
        [InlineData("", "")]
        public void Escape_HandlesSpecials(string input, string expected) =>
            Assert.Equal(expected, JsonText.Escape(input));

        [Fact]
        public void Escape_ControlCharsBelow0x20_UseUnicodeEscape() =>
            Assert.Equal("\\" + "u0001ctl", JsonText.Escape(((char)1) + "ctl"));

        [Fact]
        public void AppendQuoted_NullBecomesLiteralNull()
        {
            var sb = new System.Text.StringBuilder();
            JsonText.AppendQuoted(sb, null);
            Assert.Equal("null", sb.ToString());
        }

        [Fact]
        public void AppendQuoted_WrapsAndEscapes()
        {
            var sb = new System.Text.StringBuilder();
            JsonText.AppendQuoted(sb, "a\"b");
            Assert.Equal("\"a\\\"b\"", sb.ToString());
        }
    }
}
