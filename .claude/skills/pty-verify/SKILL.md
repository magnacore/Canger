---
name: pty-verify
description: Drive Canger in a real terminal and read back the screen, to check behaviour the unit tests cannot see — layout, colour, what a keystroke actually does. Use whenever a change is about what appears on screen, or when a defect report describes something visible.
---

# Verifying against the real screen

Canger only exists on a screen. Several defects have been invisible to the whole suite and
obvious in one screenshot: the version-control block on the status line, the preview column
collapsing, `find` narrowing the listing instead of moving the cursor. When a report describes
something *visible*, the suite is the wrong instrument.

The harness is committed at `tools/screen.py`. Do not write another one.

## Using it

```bash
python3 tools/screen.py --cwd /some/dir --settle 3 \
    --expect alpha.txt \
    --send ':find gam' --wait 2 \
    --expect betamax.txt --dump -- ./canger.sh
```

`--send --wait --expect --dump` run in the order given. `--expect` fails loudly, printing the
whole screen, if the token never appears.

For anything bespoke, import it:

```python
import sys; sys.path.insert(0, "tools")
from screen import Session

with Session(["./canger.sh"], cwd=repo) as s:
    s.settle(3)
    s.require("conflict.txt")             # the control: prove you reached the state
    s.send(":set vcs_aware true\r"); s.wait(3)
    row, col = s.require("X")
    assert s.screen.style_at(row, col) == "0;35"   # magenta, as vcsconflict should be
```

`require` waits and then raises with the screen contents. `find` returns a position or None.
`style_at` gives the SGR parameters in force for one cell. `dump` prints the numbered rows.

## The traps

Every one was learned by being fooled. They are not hypothetical.

1. **A pty has no size until you give it one.** Without `TIOCSWINSZ` the program sees 0×0 and
   lays out nothing, which reads exactly like a crash. `Session` always sets it.
2. **A line is drawn in several separately positioned pieces.** Searching the raw byte stream
   for `(git: main)` comes back empty while it is plainly on screen. Search the *rebuilt
   screen*, and prefer short tokens to phrases.
3. **Without scrolling a healthy program looks hung.** A replayer that ignores LF at the bottom
   piles everything after the first screenful onto the last row. `less` was misdiagnosed twice
   this way. `Screen` implements scrolling and the alternate screen.
4. **Text cannot prove a colour.** Use `style_at`. "`1;7;93` on one word and nothing on the
   next" is the difference between a fix and a plausible-looking one.
5. **Prove the state before measuring it.** The most expensive one. Twice a probe pressed a key,
   timed the result and published a number — and the thing it thought it had opened had never
   opened. Both numbers were withdrawn. Call `require` on something that could only be true in
   the state you meant to reach, *then* measure.

A sixth, found by the harness itself while measuring: **a read boundary lands in the middle of an
escape sequence often enough to matter.** Left unhandled the parser prints the body as text and a
row comes out as `27;1H| file-l0024.2xttxt`, which reads as a rendering bug in the program under
test. `Screen.feed` now hands back any sequence cut in half and `Session` prepends it to the next
chunk. The same is done for a UTF-8 character split across reads.

A seventh, when running ranger for comparison: unset `RANGER_LEVEL` or ranger starts with a
"nested instance" warning over the screen. `Session` clears it.

## Two things that are not traps but cost time anyway

- **`canger.sh` runs the last `./build.sh publish`, not your working tree.** Publish first or you
  will measure the previous binary. That is worth doing deliberately once: measure before,
  publish, measure after, and the difference is the evidence.
- **The user's real config is loaded**, so keys may not do what the shipped `cc.conf` says. `f`
  is a keychain prefix there (`fd fs fh fg ff fm ft`), so sending `f` then `g` runs `fg`, not
  `find`. Use `--send ':command'` through the console to test a builtin directly.
