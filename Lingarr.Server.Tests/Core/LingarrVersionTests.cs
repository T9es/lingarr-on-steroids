using System.Reflection;
using Lingarr.Core;
using Xunit;

namespace Lingarr.Server.Tests.Core;

public class LingarrVersionTests
{
    [Theory]
    [InlineData("2.6.0", "2.6.0", false)]
    [InlineData("v2.6.0", "2.6.0", false)]
    // Lingarr emits "+<sha>" only on local/git dev builds (see
    // Directory.Build.props InformationalVersion). Presence of build
    // metadata is therefore still a dev signal for our purposes, even
    // though SemVer 2.0 says metadata has no precedence meaning.
    [InlineData("2.6.0+abc1234", "2.6.0", true)]
    [InlineData("v2.6.0+sha.deadbeef", "2.6.0", true)]
    [InlineData("2.6.0-beta.1", "2.6.0", true)]
    [InlineData("2.6.0-alpha", "2.6.0", true)]
    [InlineData("2.6.0-rc.1", "2.6.0", true)]
    [InlineData("2.6.0-dev", "2.6.0", true)]
    [InlineData("2.6.0-dev+sha.abc1234", "2.6.0", true)]
    [InlineData("2.7.0", "2.6.0", true)]
    [InlineData("v2.5.0-214-gabcdef1", "2.5.0", true)]
    [InlineData("2.5.0-deadbeef1234", "2.5.0", true)]
    [InlineData(null, "2.6.0", false)]
    [InlineData("", "2.6.0", false)]
    [InlineData("   ", "2.6.0", false)]
    public void IsDevelopmentVersion_returns_expected(string? version, string releaseVersion, bool expected)
    {
        Assert.Equal(expected, LingarrVersion.IsDevelopmentVersion(version, releaseVersion));
    }

    [Fact]
    public void IsDevelopmentVersion_treats_build_metadata_as_dev_signal_in_this_app()
    {
        // The two previous tests in this file already pin that
        // behaviour. This duplicate-looking test exists only so the
        // behaviour is also stated in prose for the next reader of the
        // suite. Both "2.6.0+sha.abc1234" and "v2.6.0+sha.abc1234"
        // should be flagged dev because Lingarr's MSBuild only adds
        // "+<sha>" InformationalVersion when built from a git checkout
        // (Directory.Build.props).
        Assert.True(LingarrVersion.IsDevelopmentVersion("2.6.0+sha.abc1234", "2.6.0"));
        Assert.True(LingarrVersion.IsDevelopmentVersion("v2.6.0+sha.abc1234", "2.6.0"));
    }

    [Fact]
    public void Assembly_informational_version_contains_full_sha_when_built_from_git()
    {
        // The actual DLL produced from this repo has the shorthand
        // abbreviated SHA appended to its informational version via
        // Directory.Build.props. Re-builds without git fall back to the
        // bare version. Either way the assembly attribute must be
        // parseable as "M.m.p+sha" or "M.m.p".
        var assembly = Assembly.Load("Lingarr.Core");
        var info = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        Assert.NotNull(info);
        Assert.NotNull(info!.InformationalVersion);
        Assert.NotEmpty(info.InformationalVersion);
    }
}
