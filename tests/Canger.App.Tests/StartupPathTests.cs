// SPDX-License-Identifier: GPL-3.0-or-later
namespace Canger.App.Tests;

/// <summary>
/// What the program does with the path it is started on.
/// </summary>
/// <remarks>
/// Driven through <c>Main</c> rather than through the check itself, because the defect was that
/// the program asked a question of its own before handing the path on: <c>Tab.Enter</c> had
/// carried ranger's rule for a file argument all along, and never saw one.
/// </remarks>
public sealed class StartupPathTests : IDisposable
{
    private readonly string _root =
        Path.Join(Path.GetTempPath(), "canger-startup-" + Path.GetRandomFileName());

    public StartupPathTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Join(_root, "one.txt"), "1");
        File.WriteAllText(Path.Join(_root, "two.txt"), "2");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Runs the program with the console captured.</summary>
    private static (int Code, string Output) Run(params string[] arguments)
    {
        TextWriter output = Console.Out;
        TextWriter errors = Console.Error;
        StringWriter captured = new();

        try
        {
            Console.SetOut(captured);
            Console.SetError(captured);

            return (Program.Main(arguments), captured.ToString());
        }
        finally
        {
            Console.SetOut(output);
            Console.SetError(errors);
        }
    }

    [Fact]
    public void AFileOpensItsDirectoryWithTheCursorOnIt()
    {
        (int code, string output) = Run("--clean", "--list", Path.Join(_root, "two.txt"));

        Assert.Equal(0, code);
        Assert.Contains($"listing {_root}", output, StringComparison.Ordinal);
        Assert.Contains("> file  two.txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void ADirectoryOpensWithTheCursorWhereItWouldNormallyBe()
    {
        (int code, string output) = Run("--clean", "--list", _root);

        Assert.Equal(0, code);
        Assert.Contains("> file  one.txt", output, StringComparison.Ordinal);
    }

    [Fact]
    public void APathThatIsNotThereIsStillRefused()
    {
        (int code, string output) = Run("--clean", "--list", Path.Join(_root, "missing.txt"));

        Assert.Equal(1, code);
        Assert.Contains("no such file or directory", output, StringComparison.Ordinal);
    }
}
