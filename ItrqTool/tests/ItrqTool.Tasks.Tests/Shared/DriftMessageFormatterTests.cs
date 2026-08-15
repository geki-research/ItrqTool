using FluentAssertions;
using ItrqTool.Tasks.Shared;
using Xunit;

namespace ItrqTool.Tasks.Tests.Shared;

/// <summary>
/// Unit tests for <see cref="DriftMessageFormatter"/> — the shared rendering behind the
/// non-identical-Agree Warning raised by all three inject mappers (BLG-0079).
/// </summary>
public sealed class DriftMessageFormatterTests
{
    private const int MaxExcerpt = 160;

    // ── AreIdentical — ordinal, null-safe ────────────────────────────────────

    [Fact]
    public void AreIdentical_SameText_True()
        => DriftMessageFormatter.AreIdentical("Same", "Same").Should().BeTrue();

    [Fact]
    public void AreIdentical_BothNull_True()
        => DriftMessageFormatter.AreIdentical(null, null).Should().BeTrue();

    [Fact]
    public void AreIdentical_OneNull_False()
        => DriftMessageFormatter.AreIdentical(null, "Something").Should().BeFalse();

    // The whole reason identity is ordinal rather than "score == 1.0": the scorer lowercases and
    // collapses whitespace, so these two score 1.0 — but they ARE a change to the workbook.
    [Fact]
    public void AreIdentical_CaseOnlyDrift_False()
        => DriftMessageFormatter.AreIdentical("SAME TEXT", "same text").Should().BeFalse();

    [Fact]
    public void AreIdentical_WhitespaceOnlyDrift_False()
        => DriftMessageFormatter.AreIdentical("Same  text", "Same text").Should().BeFalse();

    // ── FormatScore — invariant culture, 4dp, null => "unknown" ──────────────

    [Fact]
    public void FormatScore_RendersFourDecimals()
        => DriftMessageFormatter.FormatScore(0.625).Should().Be("0.6250");

    // This machine runs a comma-decimal locale; a "0,63" in a log line is exactly the misreading
    // to avoid, so the separator must be a point regardless of ambient culture.
    [Fact]
    public void FormatScore_UsesInvariantCulture_PointSeparator()
    {
        DriftMessageFormatter.FormatScore(0.9091).Should().Contain(".");
        DriftMessageFormatter.FormatScore(0.9091).Should().NotContain(",");
    }

    [Fact]
    public void FormatScore_Null_RendersUnknownNotZero()
    {
        DriftMessageFormatter.FormatScore(null).Should().Be("unknown");
        DriftMessageFormatter.FormatScore(null).Should().NotBe("0.0000");
    }

    // ── Excerpts — short texts are untouched ─────────────────────────────────

    [Fact]
    public void Excerpts_ShortTexts_ReturnedWholeWithoutEllipsis()
    {
        var (cur, prev) = DriftMessageFormatter.Excerpts(
            "Number of staff at year end 2025", "Number of staff at year end 2024");

        cur.Should().Be("Number of staff at year end 2025");
        prev.Should().Be("Number of staff at year end 2024");
        cur.Should().NotContain("…");
        prev.Should().NotContain("…");
    }

    [Fact]
    public void Excerpts_FlattensWhitespaceAndTrims()
    {
        var (cur, prev) = DriftMessageFormatter.Excerpts("  a\n\tb  c  ", "a b d");

        cur.Should().Be("a b c");
        prev.Should().Be("a b d");
    }

    // ── Excerpts — the motivating case: drift at the END of a long text ──────

    [Fact]
    public void Excerpts_DriftAtEndOfLongText_WindowShowsTheDrift()
    {
        // 400 identical characters, then the only difference — the case head-truncation lost.
        var common = new string('x', 400);
        var current  = common + " as of 2025";
        var previous = common + " as of 2024";

        var (cur, prev) = DriftMessageFormatter.Excerpts(current, previous);

        cur.Should().EndWith("2025", "the window must reach the drift, not stop at char 160");
        prev.Should().EndWith("2024");
        cur.Should().NotBe(prev, "showing two identical excerpts is the bug being fixed");
        cur.Should().StartWith("…", "the elided head is marked");
        cur.Length.Should().BeLessThanOrEqualTo(MaxExcerpt + 2); // + the two ellipsis chars
    }

    // ── Excerpts — drift at the START of a long text ─────────────────────────

    [Fact]
    public void Excerpts_DriftAtStartOfLongText_WindowShowsTheDrift()
    {
        var common = new string('x', 400);
        var current  = "In 2025, " + common;
        var previous = "In 2024, " + common;

        var (cur, prev) = DriftMessageFormatter.Excerpts(current, previous);

        cur.Should().StartWith("In 2025,", "no leading ellipsis when the drift is at the head");
        prev.Should().StartWith("In 2024,");
        cur.Should().EndWith("…", "the elided tail is marked");
        cur.Should().NotBe(prev);
    }

    // ── Excerpts — prefix/suffix overlap clamp ───────────────────────────────

    [Fact]
    public void Excerpts_PrefixAndSuffixOverlap_ShortStrings_DoesNotThrow()
    {
        // "aaa" vs "aaaa": a naive scan claims 3 common leading AND 3 common trailing characters
        // out of a 3-character string. The clamp is what keeps the indices sane.
        var act = () => DriftMessageFormatter.Excerpts("aaa", "aaaa");

        var (cur, prev) = act.Should().NotThrow().Subject;
        cur.Should().Be("aaa");
        prev.Should().Be("aaaa");
    }

    [Fact]
    public void Excerpts_PrefixAndSuffixOverlap_LongRepeatedText_DoesNotThrow()
    {
        // Same overlap hazard, but long enough to actually enter the windowing path.
        var act = () => DriftMessageFormatter.Excerpts(new string('a', 300), new string('a', 301));

        var (cur, prev) = act.Should().NotThrow().Subject;
        cur.Should().NotBeNullOrEmpty();
        prev.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Excerpts_IdenticalLongTexts_DoNotThrow()
    {
        // Identical pairs never reach the Warning, but the helper must still be total.
        var text = new string('z', 500);

        var act = () => DriftMessageFormatter.Excerpts(text, text);

        act.Should().NotThrow();
    }

    // ── Excerpts — case-only drift on a long text still yields a usable window ──

    [Fact]
    public void Excerpts_CaseOnlyDriftOnLongText_ProducesUsableWindow()
    {
        var current  = new string('A', 300);
        var previous = new string('a', 300);

        var (cur, prev) = DriftMessageFormatter.Excerpts(current, previous);

        cur.Should().NotBeNullOrWhiteSpace();
        prev.Should().NotBeNullOrWhiteSpace();
        cur.Should().NotBe(prev, "the case difference must remain visible");
        cur.TrimEnd('…').Length.Should().BeLessThanOrEqualTo(MaxExcerpt);
    }

    // ── Excerpts — nulls and the overall bound ───────────────────────────────

    [Fact]
    public void Excerpts_NullText_TreatedAsEmpty_DoesNotThrow()
    {
        var act = () => DriftMessageFormatter.Excerpts(null, "Real text");

        var (cur, prev) = act.Should().NotThrow().Subject;
        cur.Should().Be("");
        prev.Should().Be("Real text");
    }

    [Fact]
    public void Excerpts_LongTexts_StayWithinTheOverallBound()
    {
        var current  = new string('x', 900) + " END-A";
        var previous = new string('x', 900) + " END-B";

        var (cur, prev) = DriftMessageFormatter.Excerpts(current, previous);

        cur.TrimStart('…').TrimEnd('…').Length.Should().BeLessThanOrEqualTo(MaxExcerpt);
        prev.TrimStart('…').TrimEnd('…').Length.Should().BeLessThanOrEqualTo(MaxExcerpt);
    }

    [Fact]
    public void Excerpts_EmbeddedNewlinesAndTabs_AreFlattenedAway()
    {
        var current  = new string('x', 200) + "\nline\ttab A";
        var previous = new string('x', 200) + "\nline\ttab B";

        var (cur, prev) = DriftMessageFormatter.Excerpts(current, previous);

        cur.Should().NotContain("\n").And.NotContain("\t");
        prev.Should().NotContain("\n").And.NotContain("\t");
    }
}
