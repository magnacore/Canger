// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.App;

namespace Canger.App.Tests;

/// <summary>
/// What <c>--config</c> says about bindings whose command it cannot find.
/// </summary>
/// <remarks>
/// A plugin's <c>OnInit</c> runs after this report, so an alias it adds is absent here and present
/// in a real session. Reporting those as broken was a false accusation against a working binding —
/// and a check that cries wolf is one people stop reading, which is how thirty-six genuinely dead
/// bindings once went unseen behind a different fault in this same report.
/// </remarks>
public class BindingCheckTests
{
    [Fact]
    public void NothingUnresolvedIsSaidPlainly()
    {
        Assert.Equal("browser bindings: every command resolves",
                     Program.BindingSummary(0, hooksPending: false));

        // Pending hooks cannot turn a clean result into a doubtful one.
        Assert.Equal("browser bindings: every command resolves",
                     Program.BindingSummary(0, hooksPending: true));
    }

    [Fact]
    public void WithNoHooksPendingTheCheckIsCertain()
    {
        // Nothing can define the name later, so the binding really is dead and the report says so.
        Assert.Contains("does not exist", Program.BindingSummary(1, hooksPending: false),
                        StringComparison.Ordinal);
    }

    [Fact]
    public void WithHooksPendingItDoesNotClaimTheBindingIsBroken()
    {
        // `map fh zi`, where a plugin's OnInit defines `zi`. The name is unresolved here and
        // perfectly good in a session.
        string summary = Program.BindingSummary(1, hooksPending: true);

        Assert.DoesNotContain("does not exist", summary, StringComparison.Ordinal);
        Assert.Contains("could not be checked", summary, StringComparison.Ordinal);
    }
}
