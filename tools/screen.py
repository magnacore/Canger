#!/usr/bin/env python3
# SPDX-License-Identifier: GPL-3.0-or-later
"""Drive Canger in a real terminal and read back what it drew.

Canger only exists on a screen: it takes over the terminal, lays out columns and reads
keystrokes.  A unit test cannot see any of that, and several defects have been invisible to the
whole suite and obvious in one screenshot -- the version-control block on the status line, the
preview column collapsing, `find` narrowing the listing instead of moving the cursor.

This opens a pty, starts a program inside it, sends keystrokes, and rebuilds the screen from the
escape stream so it can be read back as rows of text with the colour of every cell.

    python3 tools/screen.py --settle 3 --send ':find gam' --wait 2 --dump -- ./canger.sh

or, for anything bespoke, import it:

    from screen import Session
    with Session(['./canger.sh'], cwd=repo) as s:
        s.settle(3)
        s.require('alpha.txt')          # the control: prove the state you meant to reach
        s.send('fs'); s.wait(2)
        s.require('>')                  # prove the finder actually opened
        ...

THE FIVE WAYS THIS INSTRUMENT LIES TO YOU
-----------------------------------------
Every one of these was learned by being fooled by it.  They are not hypothetical.

1.  A pty has no size until you give it one.  Without TIOCSWINSZ the program sees 0x0 and lays
    out nothing, which reads exactly like a crash.  `Session` always sets it.

2.  A line is drawn in several separately positioned pieces.  Searching the raw byte stream for
    a phrase like "(git: main)" comes back empty while it is plainly on screen.  Always search
    the *rebuilt screen* (`find`, `require`), and prefer short tokens over phrases.

3.  Without scrolling, a healthy program looks hung.  A replayer that ignores LF at the bottom
    piles everything after the first screenful onto the last row.  `less` was misdiagnosed twice
    this way.  Scrolling and the alternate screen are implemented below.

4.  Text cannot prove a colour.  Read `style_at`, which gives the SGR parameters in force for a
    cell.  "1;7;93 on one word and nothing on the next" is the difference between a fix and a
    plausible-looking one.

5.  **Prove the state you are measuring, before you measure it.**  The newest and the most
    expensive.  Twice a probe pressed a key, timed the result and published a number -- and the
    thing it thought it had opened had never opened.  `require()` exists for this: it fails
    loudly rather than letting a measurement of nothing look like a measurement.

A sixth, specific to running ranger for comparison: unset RANGER_LEVEL, or ranger sees itself as
nested and starts with a warning over the screen.  `Session` clears it.
"""

from __future__ import annotations

import argparse
import fcntl
import os
import pty
import re
import select
import shutil
import signal
import struct
import sys
import termios
import time

CSI = re.compile(rb"\x1b\[([0-9;?]*)([@-~])")
OSC = re.compile(rb"\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)")


class Screen:
    """A terminal screen rebuilt from an escape stream, with per-cell style."""

    def __init__(self, rows: int, cols: int) -> None:
        self.rows, self.cols = rows, cols
        self._main = self._blank()
        self._alt = self._blank()
        self.cells = self._main
        self.row = self.col = 0
        self.style = ""
        self.top, self.bottom = 0, rows - 1

    def _blank(self):
        return [[(" ", "") for _ in range(self.cols)] for _ in range(self.rows)]

    # -- reading it back ----------------------------------------------------------------

    def line(self, row: int) -> str:
        return "".join(c for c, _ in self.cells[row]).rstrip()

    def rows_text(self, blank: bool = False) -> list[str]:
        out = [self.line(r) for r in range(self.rows)]
        return out if blank else [t for t in out if t.strip()]

    def find(self, token: str) -> tuple[int, int] | None:
        """Where a token appears on the rebuilt screen, or None."""
        for r in range(self.rows):
            c = self.line(r).find(token)
            if c >= 0:
                return r, c
        return None

    def style_at(self, row: int, col: int) -> str:
        """The SGR parameters in force for one cell -- what proves a colour claim."""
        return self.cells[row][col][1]

    # -- feeding it ---------------------------------------------------------------------

    def feed(self, data: bytes) -> None:
        i = 0
        while i < len(data):
            b = data[i]
            if b == 0x1B:
                m = OSC.match(data, i)
                if m:                                   # window titles and the like
                    i = m.end()
                    continue
                m = CSI.match(data, i)
                if m:
                    self._csi(m.group(1).decode("ascii", "replace"), m.group(2))
                    i = m.end()
                    continue
                if i + 1 < len(data) and data[i + 1] == 0x4D:   # ESC M, reverse index
                    self._reverse_index()
                    i += 2
                    continue
                i += 2                                  # charset selection and friends
                continue
            if b == 0x0D:
                self.col = 0
            elif b == 0x0A:
                self._line_feed()
            elif b == 0x08:
                self.col = max(self.col - 1, 0)
            elif b == 0x09:
                self.col = min((self.col // 8 + 1) * 8, self.cols - 1)
            elif b >= 0x20:
                end = i
                while end < len(data) and data[end] >= 0x20 and data[end] != 0x1B:
                    end += 1
                for ch in data[i:end].decode("utf-8", "replace"):
                    self._put(ch)
                i = end
                continue
            i += 1

    def _put(self, ch: str) -> None:
        if self.col >= self.cols:                       # wrap, as a terminal does
            self.col = 0
            self._line_feed()
        if 0 <= self.row < self.rows:
            self.cells[self.row][self.col] = (ch, self.style)
        self.col += 1

    def _line_feed(self) -> None:
        if self.row == self.bottom:
            self._scroll_up()
        else:
            self.row = min(self.row + 1, self.rows - 1)

    def _scroll_up(self) -> None:
        del self.cells[self.top]
        self.cells.insert(self.bottom, [(" ", "") for _ in range(self.cols)])

    def _reverse_index(self) -> None:
        if self.row == self.top:
            self.cells.insert(self.top, [(" ", "") for _ in range(self.cols)])
            del self.cells[self.bottom + 1]
        else:
            self.row = max(self.row - 1, 0)

    def _csi(self, args: str, final: bytes) -> None:
        private = args.startswith("?")
        body = args[1:] if private else args
        nums = [int(p) if p.isdigit() else 0 for p in body.split(";")] if body else []

        def n(index: int, default: int = 1) -> int:
            return nums[index] if index < len(nums) and nums[index] else default

        if private and final in (b"h", b"l"):
            if nums and nums[0] in (47, 1047, 1049):    # the alternate screen
                want_alt = final == b"h"
                target = self._alt if want_alt else self._main
                if self.cells is not target:
                    if want_alt:
                        self._alt = self._blank()
                        target = self._alt
                    self.cells = target
                    self.row = self.col = 0
            return

        if final == b"H" or final == b"f":
            self.row = min(max(n(0) - 1, 0), self.rows - 1)
            self.col = min(max(n(1) - 1, 0), self.cols - 1)
        elif final == b"A":
            self.row = max(self.row - n(0), 0)
        elif final == b"B":
            self.row = min(self.row + n(0), self.rows - 1)
        elif final == b"C":
            self.col = min(self.col + n(0), self.cols - 1)
        elif final == b"D":
            self.col = max(self.col - n(0), 0)
        elif final == b"G":
            self.col = min(max(n(0) - 1, 0), self.cols - 1)
        elif final == b"d":
            self.row = min(max(n(0) - 1, 0), self.rows - 1)
        elif final == b"r":
            self.top = min(max(n(0) - 1, 0), self.rows - 1)
            self.bottom = min(max(n(1, self.rows) - 1, 0), self.rows - 1)
        elif final == b"J":
            mode = nums[0] if nums else 0
            if mode == 2:
                self.cells[:] = self._blank()
            elif mode == 0:
                self._erase(self.row, self.col, self.rows - 1, self.cols - 1)
            elif mode == 1:
                self._erase(0, 0, self.row, self.col)
        elif final == b"K":
            mode = nums[0] if nums else 0
            if mode == 0:
                self._erase(self.row, self.col, self.row, self.cols - 1)
            elif mode == 1:
                self._erase(self.row, 0, self.row, self.col)
            else:
                self._erase(self.row, 0, self.row, self.cols - 1)
        elif final == b"m":
            self.style = "" if not body or body == "0" else body

    def _erase(self, r0: int, c0: int, r1: int, c1: int) -> None:
        for r in range(r0, r1 + 1):
            start = c0 if r == r0 else 0
            stop = c1 if r == r1 else self.cols - 1
            for c in range(start, stop + 1):
                self.cells[r][c] = (" ", "")


class Session:
    """A program running in a pty, with the screen it is drawing."""

    def __init__(self, argv, cwd=None, rows=40, cols=120, env=None):
        self.screen = Screen(rows, cols)

        # Resolved here, before the fork, because `cwd` changes directory in the child: a
        # relative `./canger.sh` would stop resolving the moment a working directory was asked
        # for, and the failure arrives as a traceback drawn onto the screen under test.
        program = os.path.abspath(shutil.which(argv[0]) or argv[0])
        argv = [program, *argv[1:]]

        self.pid, self.fd = pty.fork()
        if self.pid == 0:
            os.environ["TERM"] = "xterm-256color"
            os.environ["DOTNET_ROOT"] = os.environ.get(
                "CANGER_DOTNET_ROOT", "/opt/anaconda3/envs/dotnet/lib/dotnet")
            os.environ.pop("RANGER_LEVEL", None)        # trap 6: ranger refuses to nest
            os.environ.update(env or {})
            if cwd:
                os.chdir(os.path.expanduser(cwd))
            try:
                os.execv(argv[0], argv)
            except OSError as failure:                  # never a traceback into the screen
                os.write(2, f"screen.py: cannot run {argv[0]}: {failure}\n".encode())
            os._exit(127)
        # trap 1: without this the program sees 0x0 and lays out nothing
        fcntl.ioctl(self.fd, termios.TIOCSWINSZ, struct.pack("HHHH", rows, cols, 0, 0))
        self.closed = False

    def __enter__(self):
        return self

    def __exit__(self, *_):
        self.close()

    def wait(self, seconds: float) -> None:
        """Let the program draw for a while, keeping the screen up to date."""
        end = time.time() + seconds
        while time.time() < end:
            r, _, _ = select.select([self.fd], [], [], min(0.05, max(end - time.time(), 0)))
            if not r:
                continue
            try:
                data = os.read(self.fd, 262144)
            except OSError:
                self.closed = True
                return
            if not data:
                self.closed = True
                return
            self.screen.feed(data)

    settle = wait

    def send(self, keys: str | bytes) -> None:
        os.write(self.fd, keys.encode() if isinstance(keys, str) else keys)

    def find(self, token: str):
        return self.screen.find(token)

    def require(self, token: str, timeout: float = 5.0) -> tuple[int, int]:
        """Wait for a token to appear, and fail loudly if it never does.

        Trap 5.  Call this before every measurement, on something that proves the state you
        meant to reach -- a probe that measures a screen it never got to is worse than no
        measurement, because the number looks real.
        """
        end = time.time() + timeout
        while time.time() < end:
            found = self.screen.find(token)
            if found:
                return found
            self.wait(0.1)
        rows = "\n".join("  | " + t for t in self.screen.rows_text())
        raise AssertionError(f"never saw {token!r} on screen. What was there:\n{rows}")

    def dump(self, stream=sys.stdout, blank: bool = False) -> None:
        for i, text in enumerate(self.screen.rows_text(blank=True)):
            if blank or text.strip():
                print(f"{i:>3}|{text}", file=stream)

    def close(self) -> None:
        try:
            os.kill(self.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            os.waitpid(self.pid, 0)
        except ChildProcessError:
            pass
        try:
            os.close(self.fd)
        except OSError:
            pass


def main(argv: list[str]) -> int:
    steps: list[tuple[str, str]] = []

    class Step(argparse.Action):
        def __call__(self, parser, namespace, values, option_string=None):
            steps.append((option_string.lstrip("-"), values))

    p = argparse.ArgumentParser(
        description="Drive a terminal program and read back the screen it drew.",
        epilog="--send, --wait, --expect and --dump run in the order given.")
    p.add_argument("--rows", type=int, default=40)
    p.add_argument("--cols", type=int, default=120)
    p.add_argument("--cwd", default=None)
    p.add_argument("--settle", type=float, default=3.0,
                   help="seconds to let the program start before the first step")
    p.add_argument("--send", action=Step, help="keys to type (repeatable, ordered)")
    p.add_argument("--wait", action=Step, help="seconds to wait (repeatable, ordered)")
    p.add_argument("--expect", action=Step,
                   help="fail unless this token is on screen (repeatable, ordered)")
    p.add_argument("--dump", action=Step, nargs="?", default=argparse.SUPPRESS,
                   help="print the screen")
    p.add_argument("command", nargs=argparse.REMAINDER,
                   help="-- then the program and its arguments")
    args = p.parse_args(argv)

    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        p.error("no command given; put it after --")

    with Session(command, cwd=args.cwd, rows=args.rows, cols=args.cols) as session:
        session.settle(args.settle)
        for kind, value in steps:
            if kind == "send":
                session.send(value.replace("\\r", "\r").replace("\\e", "\x1b"))
            elif kind == "wait":
                session.wait(float(value))
            elif kind == "expect":
                row, col = session.require(value)
                print(f"  found {value!r} at row {row}, column {col}")
            elif kind == "dump":
                session.dump()
        if not steps:
            session.dump()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
