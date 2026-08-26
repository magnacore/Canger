// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Core.Model;
using Canger.Tui.Rendering;
using Canger.Tui.Text;
using Canger.Ui.Styling;

namespace Canger.Ui.Widgets;

/// <summary>
/// The command line: a single editable row at the foot of the screen.
/// </summary>
/// <remarks>
/// <para>
/// The console is where anything that needs typing happens — commands, searches, filters,
/// renames, confirmations — so it carries a full line editor: word-wise movement, kill and yank,
/// history with prefix filtering, and completion cycling.
/// </para>
/// <para>
/// It also handles character reassembly. Keys arrive as raw bytes so that bindings can match
/// non-ASCII keys, which means a multi-byte character arrives in pieces; the console is the only
/// place that needs whole characters, so it is the only place that reassembles them.
/// </para>
/// </remarks>
public sealed class ConsoleWidget(IColorScheme colorScheme) : Widget
{
    private readonly StringBuilder _pendingBytes = new();
    private readonly List<string> _completions = [];

    private string _text = string.Empty;
    private int _cursor;
    private int _completionIndex = -1;
    private string _completionSeed = string.Empty;
    private string _killRing = string.Empty;
    private string _historyPrefix = string.Empty;
    private bool _browsingHistory;

    /// <summary>Whether the console is taking input.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>What has been typed.</summary>
    public string Text => _text;

    /// <summary>Where the cursor is, as a character index into <see cref="Text"/>.</summary>
    public int CursorPosition => _cursor;

    /// <summary>What is shown before the input, normally a colon.</summary>
    public string Prompt { get; private set; } = ":";

    /// <summary>
    /// Previously entered lines.
    /// </summary>
    /// <remarks>
    /// Only lines that were actually run go in here, which is what makes it safe to write
    /// straight out to disk. Ranger keeps two copies — a working one that browsing edits and a
    /// backup of what was really executed — and saves the backup
    /// (<c>gui/widgets/console.py:281-283</c>); this needs only the one because browsing moves a
    /// position rather than modifying entries.
    /// </remarks>
    public History<string> CommandHistory { get; private set; } = new(50, unique: true);

    /// <summary>
    /// Sets how many entries are kept, discarding anything already beyond it.
    /// </summary>
    /// <param name="capacity">
    /// The limit, from <c>max_console_history_size</c>. Null or non-positive means no limit.
    /// </param>
    /// <remarks>
    /// The capacity was fixed at fifty regardless of the setting, so raising
    /// <c>max_console_history_size</c> did nothing at all.
    /// </remarks>
    public void SetHistoryCapacity(int? capacity)
    {
        int wanted = capacity is > 0 ? capacity.Value : int.MaxValue;

        if (wanted == CommandHistory.Capacity)
        {
            return;
        }

        History<string> replacement = new(wanted, unique: true);

        foreach (string entry in CommandHistory.Entries)
        {
            replacement.Add(entry);
        }

        replacement.FastForward();
        CommandHistory = replacement;
    }

    /// <summary>
    /// The question awaiting an answer, when the console is confirming something.
    /// </summary>
    public string? Question { get; private set; }

    /// <summary>The answers the question accepts, the first being the default.</summary>
    public IReadOnlyList<char> QuestionChoices { get; private set; } = [];

    /// <summary>Where the hardware cursor should sit, so the terminal shows it in the right place.</summary>
    public int ScreenCursorX =>
        Bounds.X + CellWidth.Of(Prompt) + CellWidth.Of(_text[.._cursor]);

    /// <summary>Opens the console.</summary>
    /// <param name="text">What to pre-fill the line with.</param>
    /// <param name="cursorPosition">Where to put the cursor, or -1 for the end.</param>
    /// <param name="prompt">What to show before the input.</param>
    public void Open(string text = "", int cursorPosition = -1, string prompt = ":")
    {
        ArgumentNullException.ThrowIfNull(text);

        IsOpen = true;
        IsVisible = true;
        Question = null;
        Prompt = prompt;
        _text = text;
        _cursor = cursorPosition < 0 ? text.Length : Math.Clamp(cursorPosition, 0, text.Length);
        _pendingBytes.Clear();
        ResetCompletions();
        _browsingHistory = false;

        CommandHistory.FastForward();
    }

    /// <summary>Asks the user a question, to be answered with a single key.</summary>
    /// <param name="question">What to ask.</param>
    /// <param name="choices">The accepted answers, the first being the default.</param>
    public void Ask(string question, IReadOnlyList<char>? choices = null)
    {
        ArgumentNullException.ThrowIfNull(question);

        IsOpen = true;
        IsVisible = true;
        Question = question;
        QuestionChoices = choices ?? ['y', 'n'];
        _text = string.Empty;
        _cursor = 0;
    }

    /// <summary>Closes the console.</summary>
    public void Close()
    {
        IsOpen = false;
        IsVisible = false;
        Question = null;
        _text = string.Empty;
        _cursor = 0;
        _pendingBytes.Clear();
        ResetCompletions();
    }

    /// <summary>Replaces the line.</summary>
    /// <param name="text">The new line.</param>
    /// <param name="cursorPosition">Where to put the cursor, or -1 for the end.</param>
    public void SetText(string text, int cursorPosition = -1)
    {
        ArgumentNullException.ThrowIfNull(text);

        _text = text;
        _cursor = cursorPosition < 0 ? text.Length : Math.Clamp(cursorPosition, 0, text.Length);
    }

    /// <summary>
    /// Adds a key that no console binding claimed, as typed text.
    /// </summary>
    /// <remarks>
    /// Anything not bound is text. That is what lets the console accept arbitrary input without
    /// needing a binding for every printable key.
    /// </remarks>
    /// <param name="key">The key code, which below 256 is an input byte.</param>
    public void TypeKey(int key)
    {
        if (key < 0)
        {
            return;
        }

        ResetCompletions();
        _browsingHistory = false;

        // ASCII arrives whole; anything else arrives a byte at a time and has to be reassembled.
        if (key < 0x80)
        {
            _pendingBytes.Clear();
            Insert(((char)key).ToString());
            return;
        }

        if (key > 0xFF)
        {
            return;
        }

        _pendingBytes.Append((char)key);

        byte[] bytes = [.. _pendingBytes.ToString().Select(c => (byte)c)];
        string decoded = new UTF8Encoding(false, throwOnInvalidBytes: false).GetString(bytes);

        // A replacement character means the sequence is still incomplete, so keep collecting.
        if (!decoded.Contains('�', StringComparison.Ordinal))
        {
            _pendingBytes.Clear();
            Insert(decoded);
        }
        else if (_pendingBytes.Length >= 4)
        {
            // No valid sequence is longer than four bytes, so this one is malformed.
            _pendingBytes.Clear();
        }
    }

    /// <summary>Inserts text at the cursor.</summary>
    /// <param name="text">What to insert.</param>
    public void Insert(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        _text = _text.Insert(_cursor, text);
        _cursor += text.Length;
    }

    /// <summary>Removes a character.</summary>
    /// <param name="offset">-1 for the character before the cursor, 0 for the one under it.</param>
    /// <returns>
    /// <see langword="false"/> when backspacing out of an empty line, which the caller treats as
    /// a request to close the console.
    /// </returns>
    public bool Delete(int offset)
    {
        ResetCompletions();

        if (offset < 0)
        {
            if (_cursor == 0)
            {
                // Backspacing at the start of an empty line abandons the prompt, which is how
                // the console is dismissed without reaching for Escape.
                return _text.Length != 0;
            }

            _text = _text.Remove(_cursor - 1, 1);
            _cursor--;
            return true;
        }

        if (_cursor < _text.Length)
        {
            _text = _text.Remove(_cursor, 1);
        }

        return true;
    }

    /// <summary>Moves the cursor.</summary>
    /// <param name="offset">How far, negative for leftward.</param>
    public void MoveCursor(int offset)
    {
        ResetCompletions();
        _cursor = Math.Clamp(_cursor + offset, 0, _text.Length);
    }

    /// <summary>Moves the cursor to the start or end of the line.</summary>
    /// <param name="toEnd">Whether to go to the end rather than the start.</param>
    public void MoveCursorToEdge(bool toEnd)
    {
        ResetCompletions();
        _cursor = toEnd ? _text.Length : 0;
    }

    /// <summary>Moves the cursor a word at a time.</summary>
    /// <param name="direction">Which way, negative for leftward.</param>
    public void MoveCursorByWord(int direction)
    {
        ResetCompletions();
        _cursor = FindWordBoundary(_text, _cursor, direction);
    }

    /// <summary>Deletes a word, keeping it for <see cref="Paste"/>.</summary>
    /// <param name="backward">Whether to delete the word before the cursor.</param>
    public void DeleteWord(bool backward = true)
    {
        ResetCompletions();

        int boundary = FindWordBoundary(_text, _cursor, backward ? -1 : 1);
        int start = Math.Min(boundary, _cursor);
        int end = Math.Max(boundary, _cursor);

        _killRing = _text[start..end];
        _text = _text.Remove(start, end - start);
        _cursor = start;
    }

    /// <summary>Deletes to the start or end of the line, keeping it for <see cref="Paste"/>.</summary>
    /// <param name="direction">Positive to delete to the end, negative to the start.</param>
    public void DeleteRest(int direction)
    {
        ResetCompletions();

        if (direction > 0)
        {
            _killRing = _text[_cursor..];
            _text = _text[.._cursor];
        }
        else
        {
            _killRing = _text[.._cursor];
            _text = _text[_cursor..];
            _cursor = 0;
        }
    }

    /// <summary>Inserts whatever was last deleted.</summary>
    public void Paste()
    {
        if (_killRing.Length > 0)
        {
            Insert(_killRing);
        }
    }

    /// <summary>
    /// Steps through the history, filtered by what has already been typed.
    /// </summary>
    /// <remarks>
    /// Filtering by the typed prefix is what makes the history useful rather than merely present:
    /// typing <c>cd</c> and pressing up walks only the previous <c>cd</c> commands.
    /// </remarks>
    /// <param name="direction">Which way, negative for older entries.</param>
    public void HistoryMove(int direction)
    {
        ResetCompletions();

        if (CommandHistory.IsEmpty)
        {
            return;
        }

        // The first press starts from the newest entry rather than from the one before it, so
        // pressing up once recalls the last command rather than the one before last.
        if (!_browsingHistory)
        {
            _historyPrefix = _text;
            _browsingHistory = true;
            CommandHistory.FastForward();

            if (direction < 0 && CommandHistory.Current is { } newest &&
                newest.StartsWith(_historyPrefix, StringComparison.Ordinal))
            {
                SetText(newest);
                return;
            }
        }

        string? found = _historyPrefix.Length > 0
            ? CommandHistory.Search(_historyPrefix, direction)
            : CommandHistory.Move(direction);

        if (found is not null)
        {
            SetText(found);
        }
    }

    /// <summary>
    /// Cycles through completions, offering the typed line first so it can be returned to.
    /// </summary>
    /// <param name="candidates">What the command offered.</param>
    /// <param name="direction">Which way to cycle.</param>
    public void CycleCompletions(IReadOnlyList<string> candidates, int direction)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        // Only a *new* cycle needs candidates. Once one is running the list is already held, and
        // asking again would be worse than useless: the text has been replaced by a completion,
        // so the caller recomputes from `fd_next ` rather than from `f` and gets nothing back.
        // Returning early on that emptiness is what stopped the second Tab doing anything, and
        // made completion look like a one-shot that picked the first match and stuck there.
        if (_completionIndex < 0)
        {
            if (candidates.Count == 0)
            {
                return;
            }

            _completionSeed = _text;
            _completions.Clear();
            _completions.Add(_text);
            _completions.AddRange(candidates.Where(
                c => !string.Equals(c, _text, StringComparison.Ordinal)));
            _completionIndex = 0;
        }

        _completionIndex =
            ((_completionIndex + direction) % _completions.Count + _completions.Count)
            % _completions.Count;

        _text = _completions[_completionIndex];
        _cursor = _text.Length;
    }

    /// <summary>Records the line in the history and closes the console.</summary>
    /// <returns>The line that was entered.</returns>
    public string Accept()
    {
        string line = _text;

        if (line.Trim().Length > 0)
        {
            CommandHistory.Add(line);
        }

        Close();
        return line;
    }

    /// <summary>Answers the question with a key.</summary>
    /// <param name="key">The key pressed.</param>
    /// <returns>The chosen answer, or <see langword="null"/> when the key was not one of them.</returns>
    public char? AnswerQuestion(int key)
    {
        if (Question is null)
        {
            return null;
        }

        // Enter takes the default, Escape declines, so a confirmation never traps the user.
        if (key == Core.Input.KeyCodes.Enter)
        {
            return QuestionChoices.Count > 0 ? QuestionChoices[0] : null;
        }

        if (key == Core.Input.KeyCodes.Escape)
        {
            return QuestionChoices.Count > 1 ? QuestionChoices[1] : null;
        }

        char answer = (char)key;
        return QuestionChoices.Contains(char.ToLowerInvariant(answer))
            ? char.ToLowerInvariant(answer)
            : null;
    }

    /// <inheritdoc />
    protected override void Draw(ScreenBuffer screen)
    {
        CellStyle style = colorScheme.Resolve(StyleContext.Of(ContextKey.InConsole));
        screen.Fill(Bounds.X, Bounds.Y, Bounds.Width, 1, style);

        if (Question is { } question)
        {
            string choices = QuestionChoices.Count > 0
                ? " [" + string.Join("/", QuestionChoices) + "] "
                : " ";

            screen.Write(Bounds.X, Bounds.Y,
                         new WideString(question + choices).Truncate(Bounds.Width), style);
            return;
        }

        int x = Bounds.X + screen.Write(Bounds.X, Bounds.Y, Prompt, style);
        int available = Math.Max(Bounds.Right - x, 0);

        // Scroll the line so the cursor stays visible on a long command.
        WideString wide = new(_text);
        int cursorColumn = CellWidth.Of(_text[.._cursor]);
        int offset = Math.Max(cursorColumn - available, 0);

        screen.Write(x, Bounds.Y, wide.Slice(offset, available), style);
    }

    private void ResetCompletions()
    {
        _completionIndex = -1;
        _completionSeed = string.Empty;
        _completions.Clear();
    }

    /// <summary>
    /// Finds the next word boundary, treating a run of letters or digits as a word.
    /// </summary>
    /// <param name="text">The line.</param>
    /// <param name="from">Where to start.</param>
    /// <param name="direction">Which way to look, negative for leftward.</param>
    /// <returns>The boundary position.</returns>
    public static int FindWordBoundary(string text, int from, int direction)
    {
        ArgumentNullException.ThrowIfNull(text);

        int position = Math.Clamp(from, 0, text.Length);

        if (direction < 0)
        {
            // Step over any separators first, so pressing the key twice moves two words rather
            // than stalling on the space between them.
            while (position > 0 && !char.IsLetterOrDigit(text[position - 1]))
            {
                position--;
            }

            while (position > 0 && char.IsLetterOrDigit(text[position - 1]))
            {
                position--;
            }

            return position;
        }

        while (position < text.Length && !char.IsLetterOrDigit(text[position]))
        {
            position++;
        }

        while (position < text.Length && char.IsLetterOrDigit(text[position]))
        {
            position++;
        }

        return position;
    }
}
