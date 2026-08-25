// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;

namespace Canger.Core.Tests.Configuration;

/// <summary>
/// Ranger's <c>for … in "chars": cmd(…)</c> idiom.
/// </summary>
/// <remarks>
/// The only <c>eval</c> in ranger's own shipped rc.conf, and it writes sixty chmod bindings. Any
/// configuration derived from ranger's default has it, so refusing it means ten startup warnings
/// and sixty missing keys — and Canger's own written-out equivalent used the bare <c>chmod +r</c>
/// where ranger uses <c>chmod u+r</c>, so the two were not even the same bindings.
/// </remarks>
public class EvalLoopExpansionTests
{
    [Fact]
    public void ExpandLoop_WritesOneLinePerCharacter()
    {
        IReadOnlyList<string>? lines = RangerEvalTranslator.ExpandLoop(
            """for arg in "rwxXst": cmd("map +u{0} shell -f chmod u+{0} %s".format(arg))""");

        Assert.NotNull(lines);
        Assert.Equal(6, lines.Count);
        Assert.Equal("map +ur shell -f chmod u+r %s", lines[0]);
        Assert.Equal("map +ut shell -f chmod u+t %s", lines[5]);
    }

    [Fact]
    public void ExpandLoop_SubstitutesEveryOccurrence()
    {
        // The template mentions {0} twice — once in the key, once in the command.
        IReadOnlyList<string>? lines = RangerEvalTranslator.ExpandLoop(
            """for arg in "ab": cmd("map x{0} thing {0}".format(arg))""");

        Assert.Equal(["map xa thing a", "map xb thing b"], lines);
    }

    [Fact]
    public void ExpandLoop_HandlesTheBareForm()
    {
        // ranger's `map +{0} … chmod u+{0}`: the key has no user/group letter but the command
        // still says u+. Canger's own default said a bare `chmod +r`, which is not the same.
        IReadOnlyList<string>? lines = RangerEvalTranslator.ExpandLoop(
            """for arg in "rw": cmd("map +{0}  shell -f chmod u+{0} %s".format(arg))""");

        Assert.Equal(["map +r  shell -f chmod u+r %s", "map +w  shell -f chmod u+w %s"], lines);
    }

    [Fact]
    public void ExpandLoop_IsIndifferentToSpacing()
    {
        // Spacing around the keywords, the colon and inside the call. Not around the `.` before
        // `format`, which nothing writes and the pattern deliberately does not accept.
        Assert.NotNull(RangerEvalTranslator.ExpandLoop(
            """for  arg  in  "ab" :  cmd( "map {0} x".format( arg ) )"""));
    }

    [Theory]
    // Not this shape at all.
    [InlineData("""fm.cd('/tmp')""")]
    // Iterating something other than a literal string.
    [InlineData("""for arg in items: cmd("map {0} x".format(arg))""")]
    // Formatting something other than the loop variable, so the result would be wrong.
    [InlineData("""for arg in "ab": cmd("map {0} x".format(other))""")]
    // Calling something other than cmd.
    [InlineData("""for arg in "ab": notify("{0}".format(arg))""")]
    public void ExpandLoop_RefusesAnythingElse(string code) =>
        Assert.Null(RangerEvalTranslator.ExpandLoop(code));

    [Fact]
    public void ExpandLoop_GivesNothingForAnEmptyCharacterList()
    {
        IReadOnlyList<string>? lines = RangerEvalTranslator.ExpandLoop(
            """for arg in "": cmd("map {0} x".format(arg))""");

        Assert.NotNull(lines);
        Assert.Empty(lines);
    }
}
