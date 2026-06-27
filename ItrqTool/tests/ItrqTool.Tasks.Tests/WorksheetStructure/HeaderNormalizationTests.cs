using FluentAssertions;
using ItrqTool.Tasks.WorksheetStructure;
using Xunit;

namespace ItrqTool.Tasks.Tests.WorksheetStructure;

public sealed class HeaderNormalizationTests
{
    [Fact]
    public void CollapsesAllWhitespace_TrimsAndUppercases()
    {
        HeaderNormalization.Normalize("  Foo\r\nBar  Baz \t qux ")
            .Should().Be("FOO BAR BAZ QUX");
    }

    [Fact]
    public void CrLfAndLfNormalizeIdentically()
    {
        HeaderNormalization.Normalize("Answer\r\nStatus")
            .Should().Be(HeaderNormalization.Normalize("Answer\nStatus"));
    }

    [Fact]
    public void DownArrowGlyphIsPreserved()
    {
        HeaderNormalization.Normalize("↓ Answer ↓").Should().Be("↓ ANSWER ↓");
    }

    [Fact]
    public void NullAndWhitespace_NormalizeToEmpty()
    {
        HeaderNormalization.Normalize(null).Should().BeEmpty();
        HeaderNormalization.Normalize("   ").Should().BeEmpty();
    }
}
