// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Configuration;

namespace Canger.Core.Tests.Configuration;

/// <summary>
/// Translating the Python bindings ranger ships into Canger's own.
/// </summary>
/// <remarks>
/// This exists because ranger binds Escape and Enter — among most of the console — to
/// <c>eval fm.ui.console.…</c>. A configuration carried over from ranger overrides Canger's
/// working bindings with Python that cannot run, and the result is a prompt with no way out.
/// </remarks>
public class RangerEvalTranslatorTests
{
    [Theory]
    [InlineData("eval fm.ui.console.close()", "console_close")]
    [InlineData("eval fm.ui.console.close(True)", "console_close")]
    [InlineData("eval fm.ui.console.execute()", "console_accept")]
    [InlineData("eval fm.ui.console.tab()", "console_complete")]
    [InlineData("eval fm.ui.console.tab(-1)", "console_complete_back")]
    [InlineData("eval fm.ui.console.delete(-1)", "console_delete_back")]
    [InlineData("eval fm.ui.console.delete(0)", "console_delete")]
    [InlineData("eval fm.ui.console.paste()", "console_paste")]
    public void Translate_HandlesTheConsoleBindingsThatWouldOtherwiseTrapTheUser(
        string action, string expected)
    {
        Assert.Equal(expected, RangerEvalTranslator.Translate(action));
    }

    [Theory]
    [InlineData("eval fm.ui.console.move(right=0, absolute=True)", "console_home")]
    [InlineData("eval fm.ui.console.move(right=-1, absolute=True)", "console_end")]
    public void Translate_IgnoresHowTheOriginalWasSpaced(string action, string expected)
    {
        // A hand-edited line should still resolve, so whitespace inside the call is not
        // significant.
        Assert.Equal(expected, RangerEvalTranslator.Translate(action));
    }

    [Theory]
    [InlineData("eval -q fm.ui.taskview.task_move(-1)", "task_move_down")]
    [InlineData("eval fm.ui.taskview.task_move(0)", "task_move_up")]
    [InlineData("eval fm.ui.taskview.task_remove()", "task_remove")]
    public void Translate_StripsTheQuietFlagWhichHasNothingToSuppress(string action,
                                                                     string expected)
    {
        Assert.Equal(expected, RangerEvalTranslator.Translate(action));
    }

    [Theory]
    [InlineData("eval fm.cut(dirarg=dict(down=1), narg=quantifier)", "cut down=1")]
    [InlineData("eval fm.copy(dirarg=dict(to=-1), narg=quantifier)", "copy to=-1")]
    public void Translate_HandlesTheDirectionalClipboardForms(string action, string expected)
    {
        Assert.Equal(expected, RangerEvalTranslator.Translate(action));
    }

    [Theory]
    [InlineData("quit")]
    [InlineData("move down=1")]
    [InlineData("shell -f chmod u+x %s")]
    public void Translate_LeavesSomethingThatIsNotAnEvalAlone(string action)
    {
        Assert.Null(RangerEvalTranslator.Translate(action));
    }

    [Fact]
    public void Translate_LeavesAnEvalItDoesNotRecogniseAlone()
    {
        // The table covers ranger's own forms. A user's own C# must reach the eval machinery.
        Assert.Null(RangerEvalTranslator.Translate("""eval fm.Notify("mine");"""));
    }

    [Theory]
    [InlineData("eval fm.open_console('rename ' + fm.thisfile.relative_path)",
                "rename_append where=end")]
    [InlineData("eval fm.open_console('rename ' + fm.thisfile.relative_path, position=7)",
                "rename_append where=start")]
    public void TranslateParameterised_ReadsWhereTheCursorShouldLand(string action,
                                                                    string expected)
    {
        // position=7 is the length of "rename ", so the cursor sits at the start of the name.
        Assert.Equal(expected, RangerEvalTranslator.TranslateParameterised(action));
    }

    [Fact]
    public void TranslateParameterised_KeepsThePathOutOfAMountPointBinding()
    {
        Assert.Equal("cd /run/media/$USER", RangerEvalTranslator.TranslateParameterised(
            "eval fm.cd('/run/media/' + os.getenv('USER'))"));
    }

    [Fact]
    public void TranslateParameterised_StillHandlesTheFixedForms()
    {
        Assert.Equal("console_close",
                     RangerEvalTranslator.TranslateParameterised("eval fm.ui.console.close()"));
    }

    [Fact]
    public void TranslateParameterised_KeepsThePathOfATabOpenedAtAFixedPlace()
    {
        // This is how a configuration keeps a set of working directories one keystroke away.
        Assert.Equal("tab_new ~/Projects label=Projects",
                     RangerEvalTranslator.TranslateParameterised(
                         """eval fm.tab_new(narg='Projects', path="~/Projects")"""));
    }

    [Fact]
    public void TranslateParameterised_HandlesAPathContainingSpaces()
    {
        Assert.Equal("tab_new ~/Documents/NOTES ARCHIVE label=Notes",
                     RangerEvalTranslator.TranslateParameterised(
                         """eval fm.tab_new(narg='Notes', path="~/Documents/NOTES ARCHIVE")"""));
    }

    [Fact]
    public void TranslateParameterised_HandlesATabOpenedAtTheCurrentDirectory()
    {
        Assert.Equal("tab_new %d",
                     RangerEvalTranslator.TranslateParameterised("eval fm.tab_new('%d')"));
    }

    [Fact]
    public void TranslateParameterised_PointsRangersInstallDirectoryAtCangers()
    {
        Assert.Equal("cd $CANGER_INSTALL_DIR",
                     RangerEvalTranslator.TranslateParameterised("eval fm.cd(ranger.RANGERDIR)"));
    }
}
