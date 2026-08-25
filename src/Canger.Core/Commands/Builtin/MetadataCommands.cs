// SPDX-License-Identifier: GPL-3.0-or-later
using Canger.Core.Model;
using Canger.Core.State;

namespace Canger.Core.Commands.Builtin;

/// <summary>
/// Asks for one metadata field after another, filling the prompt with each in turn.
/// </summary>
/// <remarks>
/// Annotating a file means several fields, and typing <c>:meta</c> afresh for each is tedious.
/// This queues the keys and re-opens the prompt as each answer is given, so a whole record is
/// entered in one pass.
/// </remarks>
[Command("prompt_metadata",
         Summary = "Ask for several metadata fields in turn: prompt_metadata <key>...")]
public class PromptMetadataCommand : CangerCommand
{
    /// <summary>
    /// The keys still to be asked about.
    /// </summary>
    /// <remarks>
    /// Shared between this command and <c>:meta</c> because the two hand control back and forth:
    /// each answer to <c>:meta</c> continues the chain this command started. It lives on the
    /// registry rather than in a static so that two file managers in one process — which the
    /// tests routinely create — do not interfere with each other.
    /// </remarks>
    private readonly Stack<string> _pending = [];

    /// <summary>The command the prompt is refilled with.</summary>
    protected virtual string PromptCommand => "meta";

    /// <inheritdoc />
    public override void Execute()
    {
        Chain.Clear();

        // Reversed, so popping asks for them in the order they were written.
        foreach (string key in Words().Skip(1).Reverse())
        {
            Chain.Push(key);
        }

        ContinueChain();
    }

    /// <summary>Asks about the next queued key, if there is one.</summary>
    protected void ContinueChain()
    {
        if (Chain.Count == 0)
        {
            return;
        }

        string key = Chain.Pop();
        string existing = FileManager.CurrentFile is { } file
            ? FileManager.Metadata.Get(file.Path)[key]
            : string.Empty;

        string text = $"{PromptCommand} {key} {existing}";
        FileManager.OpenConsole(text, text.Length);
    }

    /// <summary>The queue of keys, kept where both commands can reach it.</summary>
    private Stack<string> Chain =>
        FileManager.Commands.State(nameof(PromptMetadataCommand), () => _pending);

    /// <summary>The words of the line.</summary>
    private IEnumerable<string> Words()
    {
        for (int i = 0; Argument(i).Length > 0; i++)
        {
            yield return Argument(i);
        }
    }
}

/// <summary>
/// Records a metadata field against the selection, or removes it when given no value.
/// </summary>
[Command("meta", Summary = "Annotate the selection: meta <key> [<value>]")]
public sealed class MetaCommand : PromptMetadataCommand
{
    /// <inheritdoc />
    public override void Execute()
    {
        string key = Argument(1);

        if (key.Length == 0)
        {
            FileManager.Notify("Usage: meta <key> [<value>]", isError: true);
            return;
        }

        // An empty value deletes the field, which is the only way to unset one.
        Dictionary<string, string> update = new(StringComparer.Ordinal) { [key] = Rest(2) };

        foreach (FsNode entry in FileManager.Selection)
        {
            FileManager.Metadata.Set(entry.Path, update);
        }

        // Whatever queued this prompt gets the next field, so a chain runs to its end.
        ContinueChain();
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> Complete(int direction)
    {
        if (FileManager.CurrentFile is not { } file)
        {
            return [];
        }

        string key = Argument(1);
        FileMetadata metadata = FileManager.Metadata.Get(file.Path);

        // Completing a key that already has a value offers that value, so it can be edited
        // rather than retyped.
        if (metadata.Has(key))
        {
            return [$"{Argument(0)} {key} {metadata[key]}"];
        }

        return
        [
            .. metadata.Keys
                .Where(k => k.StartsWith(key, StringComparison.Ordinal))
                .Select(k => $"{Argument(0)} {k}")
        ];
    }
}
