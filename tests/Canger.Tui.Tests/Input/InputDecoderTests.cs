// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;
using Canger.Core.Input;
using Canger.Tui.Input;

namespace Canger.Tui.Tests.Input;

public class InputDecoderTests
{
    private static IReadOnlyList<InputEvent> Decode(string input) =>
        new InputDecoder().Feed(Encoding.UTF8.GetBytes(input));

    private static int[] Keys(IEnumerable<InputEvent> events) =>
        [.. events.OfType<KeyEvent>().Select(e => e.Key)];

    [Fact]
    public void Feed_ReportsOrdinaryKeysAsThemselves() =>
        Assert.Equal([(int)'h', (int)'j', (int)'k'], Keys(Decode("hjk")));

    [Fact]
    public void Feed_ReportsControlKeys()
    {
        // Ctrl-A arrives as byte 1, which is exactly what <C-a> binds to, so the decoder and
        // the binding parser meet in the middle.
        Assert.Equal([1], Keys(Decode("\u0001")));
        Assert.Equal([3], Keys(Decode("\u0003")));
        Assert.Equal(KeyBindingParser.Parse("<C-a>"), Keys(Decode("\u0001")));
    }

    [Fact]
    public void Feed_ReportsNonAsciiAsItsUtf8Bytes()
    {
        // Bindings match raw bytes, so the decoder must not reassemble characters. The console
        // does that itself, and it is the only part that needs whole characters.
        Assert.Equal([0xC3, 0xB6], Keys(Decode("ö")));
    }

    [Theory]
    [InlineData("\e[A", KeyCodes.Up)]
    [InlineData("\e[B", KeyCodes.Down)]
    [InlineData("\e[C", KeyCodes.Right)]
    [InlineData("\e[D", KeyCodes.Left)]
    [InlineData("\e[H", KeyCodes.Home)]
    [InlineData("\e[F", KeyCodes.End)]
    [InlineData("\e[Z", KeyCodes.ShiftTab)]
    public void Feed_DecodesCursorKeys(string input, int expected) =>
        Assert.Equal([expected], Keys(Decode(input)));

    [Theory]
    [InlineData("\eOA", KeyCodes.Up)]
    [InlineData("\eOB", KeyCodes.Down)]
    [InlineData("\eOP", 265)]
    [InlineData("\eOS", 268)]
    public void Feed_DecodesTheAlternativeCursorKeyEncoding(string input, int expected) =>
        // Terminals switch to this form in application keypad mode.
        Assert.Equal([expected], Keys(Decode(input)));

    [Theory]
    [InlineData("\e[2~", KeyCodes.Insert)]
    [InlineData("\e[3~", KeyCodes.Delete)]
    [InlineData("\e[5~", KeyCodes.PageUp)]
    [InlineData("\e[6~", KeyCodes.PageDown)]
    [InlineData("\e[15~", 269)]
    [InlineData("\e[24~", 276)]
    public void Feed_DecodesNumberedKeys(string input, int expected) =>
        Assert.Equal([expected], Keys(Decode(input)));

    [Fact]
    public void Feed_DecodesShiftDelete() =>
        Assert.Equal([KeyCodes.ShiftDelete], Keys(Decode("\e[3;2~")));

    [Fact]
    public void Feed_FallsBackToTheUnmodifiedKeyForOtherModifiers() =>
        // Ctrl-Up. curses reports the plain key for these too.
        Assert.Equal([KeyCodes.Up], Keys(Decode("\e[1;5A")));

    [Fact]
    public void Feed_DecodesAltAsAMarkerFollowedByTheKey()
    {
        // This is exactly what <A-j> parses to, so the two meet in the middle.
        Assert.Equal([KeyCodes.Alt, (int)'j'], Keys(Decode("\ej")));
        Assert.Equal(KeyBindingParser.Parse("<A-j>"), Keys(Decode("\ej")));
    }

    [Fact]
    public void Feed_HoldsALoneEscapeUntilItIsSureWhatItIs()
    {
        // ESC may be the Escape key or the start of a sequence whose rest has not arrived.
        InputDecoder decoder = new();

        Assert.Empty(decoder.Feed("\e"u8));
        Assert.True(decoder.HasPendingInput);
    }

    [Fact]
    public void Flush_ResolvesAHeldEscapeIntoTheEscapeKey()
    {
        InputDecoder decoder = new();
        decoder.Feed("\e"u8);

        Assert.Equal([KeyCodes.Escape], Keys(decoder.Flush()));
        Assert.False(decoder.HasPendingInput);
    }

    [Fact]
    public void Feed_ResolvesAHeldEscapeWhenTheRestOfTheSequenceArrives()
    {
        // Escape sequences are routinely split across reads, so a sequence must decode the same
        // whether it arrives whole or a byte at a time.
        InputDecoder decoder = new();

        Assert.Empty(decoder.Feed("\e"u8));
        Assert.Empty(decoder.Feed("["u8));
        Assert.Equal([KeyCodes.Up], Keys(decoder.Feed("A"u8)));
    }

    [Fact]
    public void Feed_DecodesASequenceSplitAcrossManyReads()
    {
        InputDecoder decoder = new();
        List<InputEvent> events = [];

        foreach (byte value in Encoding.ASCII.GetBytes("\e[15~"))
        {
            events.AddRange(decoder.Feed([value]));
        }

        Assert.Equal([269], Keys(events));
    }

    [Fact]
    public void Feed_DecodesSeveralEventsFromOneRead() =>
        Assert.Equal([(int)'j', KeyCodes.Up, (int)'k'], Keys(Decode("j\e[Ak")));

    [Fact]
    public void Feed_DecodesAMousePress()
    {
        MouseEvent mouse = Assert.IsType<MouseEvent>(Assert.Single(Decode("\e[<0;12;5M")));

        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(MouseAction.Press, mouse.Action);
        // The terminal counts from one; everything above the decoder counts from zero.
        Assert.Equal(11, mouse.X);
        Assert.Equal(4, mouse.Y);
    }

    [Fact]
    public void Feed_DecodesAMouseRelease()
    {
        MouseEvent mouse = Assert.IsType<MouseEvent>(Assert.Single(Decode("\e[<0;1;1m")));

        Assert.Equal(MouseAction.Release, mouse.Action);
        Assert.Equal(0, mouse.X);
        Assert.Equal(0, mouse.Y);
    }

    [Theory]
    [InlineData(64, MouseButton.WheelUp)]
    [InlineData(65, MouseButton.WheelDown)]
    [InlineData(0, MouseButton.Left)]
    [InlineData(1, MouseButton.Middle)]
    [InlineData(2, MouseButton.Right)]
    public void Feed_DecodesEachMouseButton(int code, MouseButton expected)
    {
        MouseEvent mouse = Assert.IsType<MouseEvent>(
            Assert.Single(Decode($"\e[<{code};1;1M")));

        Assert.Equal(expected, mouse.Button);
    }

    [Fact]
    public void Feed_DecodesMouseModifiers()
    {
        // 0 + shift(4) + alt(8) + control(16) = 28
        MouseEvent mouse = Assert.IsType<MouseEvent>(Assert.Single(Decode("\e[<28;1;1M")));

        Assert.True(mouse.Shift);
        Assert.True(mouse.Alt);
        Assert.True(mouse.Control);
    }

    [Fact]
    public void Feed_DecodesADrag()
    {
        MouseEvent mouse = Assert.IsType<MouseEvent>(Assert.Single(Decode("\e[<32;3;4M")));

        Assert.Equal(MouseAction.Drag, mouse.Action);
    }

    [Fact]
    public void Feed_DecodesMouseCoordinatesBeyond223Columns()
    {
        // The original mouse encoding packed coordinates into single bytes and could not report
        // past column 223. SGR reporting is used precisely so wide terminals work.
        MouseEvent mouse = Assert.IsType<MouseEvent>(Assert.Single(Decode("\e[<0;300;120M")));

        Assert.Equal(299, mouse.X);
        Assert.Equal(119, mouse.Y);
    }

    [Fact]
    public void Feed_DeliversPastedTextAsOneEvent()
    {
        // Pasted text must never be mistaken for typing: a pasted "q" would otherwise quit.
        PasteEvent paste = Assert.IsType<PasteEvent>(
            Assert.Single(Decode("\e[200~hello world\e[201~")));

        Assert.Equal("hello world", paste.Text);
    }

    [Fact]
    public void Feed_KeepsControlCharactersInsidePastedText()
    {
        PasteEvent paste = Assert.IsType<PasteEvent>(
            Assert.Single(Decode("\e[200~line one\nline two\e[201~")));

        Assert.Equal("line one\nline two", paste.Text);
    }

    [Fact]
    public void Feed_DecodesPastedTextAsCharactersNotBytes()
    {
        // Paste content is text, unlike key presses, so it is decoded as UTF-8.
        PasteEvent paste = Assert.IsType<PasteEvent>(
            Assert.Single(Decode("\e[200~naïve モヒカン\e[201~")));

        Assert.Equal("naïve モヒカン", paste.Text);
    }

    [Fact]
    public void Feed_HandlesAPasteSplitAcrossReads()
    {
        InputDecoder decoder = new();
        List<InputEvent> events = [];

        events.AddRange(decoder.Feed("\e[200~hel"u8));
        events.AddRange(decoder.Feed("lo"u8));
        events.AddRange(decoder.Feed("\e[201~"u8));

        Assert.Equal("hello", Assert.IsType<PasteEvent>(Assert.Single(events)).Text);
    }

    [Fact]
    public void Feed_ResumesNormalDecodingAfterAPaste()
    {
        IReadOnlyList<InputEvent> events = Decode("\e[200~x\e[201~j");

        Assert.Equal(2, events.Count);
        Assert.Equal("x", Assert.IsType<PasteEvent>(events[0]).Text);
        Assert.Equal((int)'j', Assert.IsType<KeyEvent>(events[1]).Key);
    }

    [Fact]
    public void Feed_IgnoresSequencesItDoesNotRecognise() =>
        // An unknown sequence is dropped rather than typed as text, so a terminal reporting
        // something Canger does not handle cannot insert junk into a filename.
        Assert.Empty(Decode("\e[>1;2c"));
}
