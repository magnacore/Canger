// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.FileSystem;

namespace Canger.Core.Tests.FileSystem;

/// <summary>
/// Expanding a path the way a configuration file writes it.
/// </summary>
/// <remarks>
/// Canger expanded <c>~</c> only in <c>:cd</c>. Everything else — <c>tab_new</c> above all — took
/// the tilde literally, so <c>eval fm.tab_new(path="~/Books")</c> opened a tab on a directory
/// named <c>~</c> beneath the current one. Thirteen bindings in a real configuration, every one of
/// them opening an unusable tab that said only "not accessible".
/// </remarks>
public class UserPathTests
{
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [Fact]
    public void Expand_ResolvesALeadingTilde() =>
        Assert.Equal(Path.Join(Home, "Books"), UserPath.Expand("~/Books"));

    [Fact]
    public void Expand_ResolvesATildeOnItsOwn() => Assert.Equal(Home, UserPath.Expand("~"));

    [Fact]
    public void Expand_KeepsSpacesInAPath()
    {
        // Every one of the real bindings has spaces: "~/Productivity_System/04 BIN".
        Assert.Equal(Path.Join(Home, "04 BIN", "RA RP SP"),
                     UserPath.Expand("~/04 BIN/RA RP SP"));
    }

    [Fact]
    public void Expand_LeavesATildeInsideAPathAlone()
    {
        // A tilde is an ordinary character anywhere but the front, and real filenames contain it.
        Assert.Equal("/tmp/back~up", UserPath.Expand("/tmp/back~up"));
    }

    [Fact]
    public void Expand_SubstitutesAnEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("CANGER_TEST_DIR", "/tmp/canger-test-dir");

        try
        {
            Assert.Equal("/tmp/canger-test-dir/inside",
                         UserPath.Expand("$CANGER_TEST_DIR/inside"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CANGER_TEST_DIR", null);
        }
    }

    [Fact]
    public void Expand_SubstitutesABracedVariable()
    {
        Environment.SetEnvironmentVariable("CANGER_TEST_DIR", "/tmp/braced");

        try
        {
            Assert.Equal("/tmp/braced/x", UserPath.Expand("${CANGER_TEST_DIR}/x"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CANGER_TEST_DIR", null);
        }
    }

    [Fact]
    public void Expand_MakesARelativePathAbsoluteAgainstWhatItWasGiven() =>
        Assert.Equal("/tmp/base/sub", UserPath.Expand("sub", "/tmp/base"));

    [Fact]
    public void Expand_LeavesAnAbsolutePathAlone() =>
        Assert.Equal("/etc/hosts", UserPath.Expand("/etc/hosts", "/tmp/base"));

    [Fact]
    public void ExpandVariables_LeavesALoneDollarAsWritten() =>
        Assert.Equal("/tmp/a$", UserPath.ExpandVariables("/tmp/a$"));

    [Fact]
    public void ExpandVariables_TreatsAnUnsetVariableAsEmpty()
    {
        // As a shell does. The caller then reports that the path does not exist, which is more
        // use than silently going somewhere else.
        Assert.Equal("/x/", UserPath.ExpandVariables("/x/$CANGER_DEFINITELY_UNSET"));
    }
}
