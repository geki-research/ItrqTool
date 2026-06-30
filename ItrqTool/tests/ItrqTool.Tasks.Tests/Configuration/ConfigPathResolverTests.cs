using FluentAssertions;
using ItrqTool.Tasks.Configuration;
using Xunit;

namespace ItrqTool.Tasks.Tests.Configuration;

public sealed class ConfigPathResolverTests
{
    [Theory]
    [InlineData(@"C:\x\y.json")]
    [InlineData(@"C:\configs\clq-inject-config.json")]
    public void Resolve_AbsolutePath_ReturnedUnchanged(string absolutePath)
    {
        var result = ConfigPathResolver.Resolve(absolutePath);
        result.Should().Be(absolutePath);
    }

    [Theory]
    [InlineData("configs/clq-inject-config.json")]
    [InlineData(@"configs\rlq-config.json")]
    public void Resolve_RelativePath_CombinedWithBaseDirectory(string relativePath)
    {
        var expected = Path.Combine(AppContext.BaseDirectory, relativePath);

        var result = ConfigPathResolver.Resolve(relativePath);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_EmptyOrWhitespace_ReturnedUnchanged(string path)
    {
        var result = ConfigPathResolver.Resolve(path);
        result.Should().Be(path);
    }
}
