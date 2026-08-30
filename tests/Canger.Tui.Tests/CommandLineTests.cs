// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Tui;

namespace Canger.Tui.Tests;

/// <summary>
/// Deciding when a command line can be run without a shell.
/// </summary>
/// <remarks>
/// <para>
/// Running everything as <c>sh -c "the whole line"</c> makes the line one argument, and Linux caps
/// one argument at 131 072 bytes however much room argv has in total. Numbering 2 561 files built
/// a line of 380 530 bytes and failed before the program was reached.
/// </para>
/// <para>
/// The cost of splitting a line wrongly is running something other than what was asked, so the
/// rule refuses whenever it cannot be certain. Most of what follows is about what it refuses.
/// </para>
/// </remarks>
public class CommandLineTests
{
    private static string[] Split(string command) =>
        CommandLine.TrySplit(command) is { } words ? [.. words] : [];

    [Fact]
    public void APlainCommandIsSplitIntoItsWords()
    {
        Assert.Equal(["file-number", "1", "3"], Split("file-number 1 3"));
    }

    [Fact]
    public void QuotedFilenamesKeepTheirSpaces()
    {
        // Every filename Canger passes is quoted as the macro expands, so this is the shape of
        // nearly every line it builds.
        Assert.Equal(["file-number", "1", "3", "/a/01 TASK CAPTURE BIN/x.md"],
                     Split("file-number 1 3 '/a/01 TASK CAPTURE BIN/x.md'"));
    }

    [Fact]
    public void TwoThousandFilenamesAreStillJustWords()
    {
        // The case that started this. As one argument it is three times over the limit; as words
        // it is a fifth of what argv allows.
        // Names of the length actually seen: 82 characters on average, under a directory whose
        // own name is long. Short ones would not reach the limit and the test would prove nothing.
        const string Directory = "/home/manuj/Productivity_System/01 TASK CAPTURE BIN/1 ARTICLES";

        string[] paths =
        [
            .. Enumerable.Range(0, 2561)
                         .Select(i => $"'{Directory}/an article with a fairly long name {i:D4}.md'"),
        ];

        string line = "file-number 1 3 " + string.Join(' ', paths);

        Assert.True(line.Length > 131_072,
                    $"the line is {line.Length} bytes and has to exceed the per-argument limit");

        string[] words = Split(line);

        Assert.Equal(2564, words.Length);
        Assert.Equal($"{Directory}/an article with a fairly long name 0000.md", words[3]);
    }

    [Theory]
    [InlineData("ls | less")]
    [InlineData("ls > out.txt")]
    [InlineData("ls < in.txt")]
    [InlineData("ls && rm x")]
    [InlineData("ls; rm x")]
    [InlineData("ls &")]
    [InlineData("echo $HOME")]
    [InlineData("echo `date`")]
    [InlineData("rm *.txt")]
    [InlineData("rm file?.txt")]
    [InlineData("rm file[12].txt")]
    [InlineData("echo {a,b}")]
    [InlineData("cd ~/notes")]
    [InlineData("echo hi # a comment")]
    [InlineData("(cd /tmp && ls)")]
    [InlineData("echo !!")]
    public void AnythingThatNeedsAShellGetsOne(string command)
    {
        Assert.Null(CommandLine.TrySplit(command));
    }

    [Fact]
    public void ADoubleQuotedStringWithAVariableGoesToTheShell()
    {
        // "$HOME/x" expands there and would not here.
        Assert.Null(CommandLine.TrySplit("ls \"$HOME/notes\""));
    }

    [Fact]
    public void APlainDoubleQuotedStringIsFine()
    {
        Assert.Equal(["ls", "my notes"], Split("ls \"my notes\""));
    }

    [Fact]
    public void AnUnclosedQuoteGoesToTheShell()
    {
        // The shell reports it far better than a guess would.
        Assert.Null(CommandLine.TrySplit("ls 'unterminated"));
        Assert.Null(CommandLine.TrySplit("ls \"unterminated"));
    }

    [Fact]
    public void AnEmptyLineHasNoProgramToRun()
    {
        Assert.Null(CommandLine.TrySplit(string.Empty));
        Assert.Null(CommandLine.TrySplit("   \t "));
    }

    [Fact]
    public void RunsOfWhitespaceSeparateWordsAsAShellWould()
    {
        Assert.Equal(["a", "b", "c"], Split("  a   b \t c  "));
    }

    [Fact]
    public void QuotesJoinedToAWordBecomeOneWord()
    {
        // `--name='two words'` is one argument to the program, not two.
        Assert.Equal(["x", "--name=two words"], Split("x --name='two words'"));
    }

    [Fact]
    public void AnEmptyQuotedArgumentSurvives()
    {
        // A shell passes an empty string through as a real argument, and dropping it would shift
        // every argument after it by one.
        Assert.Equal(["x", "", "y"], Split("x '' y"));
    }

    [Fact]
    public void AQuoteInsideASingleQuotedStringIsLiteral()
    {
        // ShellQuote writes an embedded apostrophe as '\'' — four characters that a shell reads
        // as one. Reading it here would need the same trick, and getting it subtly wrong would
        // rename the wrong file, so the backslash sends the whole line to the shell instead.
        Assert.Null(CommandLine.TrySplit(@"rm 'it'\''s.md'"));
    }

    [Theory]
    [InlineData("cd /tmp")]
    [InlineData("export FOO=1")]
    [InlineData("alias x=y")]
    [InlineData("ulimit -n")]
    [InlineData("source /etc/profile")]
    public void AShellBuiltinWithNoProgramBehindItGoesToTheShell(string command)
    {
        // These have no executable file anywhere, so starting them directly would report that
        // there is no such program where a shell would simply have run them.
        Assert.Null(CommandLine.TrySplit(command));
    }

    [Theory]
    [InlineData("echo hello")]
    [InlineData("printf %s x")]
    [InlineData("test -f /etc/passwd")]
    [InlineData("pwd")]
    public void ABuiltinThatAlsoExistsAsAProgramStillGoesToTheShell(string command)
    {
        // A file does exist for these, but it is not the same thing: the shell's echo, printf and
        // test differ from the ones in /usr/bin in small ways somebody's binding may depend on.
        // Nothing is lost by sending them to the shell, since no builtin is ever handed two
        // thousand filenames.
        Assert.Null(CommandLine.TrySplit(command));
    }

    [Fact]
    public void AProgramWhoseNameMerelyContainsABuiltinIsNotOne()
    {
        Assert.Equal(["echoserver", "--port", "80"], Split("echoserver --port 80"));
    }
}
