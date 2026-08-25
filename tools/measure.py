#!/usr/bin/env python3
"""Measures how long Canger takes to do the things a user waits for.

Canger exists for snappiness, and for a long time nothing here was measured. Worse, everything
that *was* measured had been measured against a Debug build, because `canger.sh` pointed at
`bin/Debug`. This script exists so that never happens silently again: it names the binary it ran,
and it refuses to guess.

    tools/measure.py                          # the default binary canger.sh would pick
    tools/measure.py --binary path/to/canger  # a specific build, to compare two of them
    tools/measure.py --compare                # Debug against the published build, side by side

Three things it is careful about, each of which has produced a false reading in this project
before:

* **The window size must be set explicitly.** A freshly forked pty reports 0x0, and Canger then
  lays nothing out — the run looks instant because it drew nothing.
* **The first run of a process is not representative.** Cold JIT and a cold page cache once made a
  folder look four times slower than it was. The first result of every case is discarded.
* **A mean hides a scheduling hiccup; a min hides nothing.** Both min and median are reported.

It always points `--datadir` at a scratch directory, so it can never disturb the real bookmarks,
tags or console history.
"""
import argparse
import fcntl
import os
import pty
import select
import shutil
import struct
import subprocess
import sys
import tempfile
import termios
import time

ROWS, COLS = 40, 140

# Previews are generated on a worker and then ask for a redraw, so "the screen went quiet" can be
# waiting on a `bash scope.sh` subprocess rather than on Canger. Measuring with them on made a
# 20,546-entry directory look *faster* to start than a 5-entry one, which is impossible — the
# small directory's files had previews and the large one's settle happened to land in a gap. Both
# numbers are reported, and previews are off unless asked for, because otherwise the headline is
# a bash startup.
NO_PREVIEW = ["--cmd", "set preview_files false",
              "--cmd", "set preview_directories false"]

# How long the screen must stay quiet before a frame counts as settled. Long enough not to cut a
# frame in half, short enough not to dominate the number being measured.
QUIET = 0.35

# Anything slower than this is a hang, not a measurement.
DEADLINE = 20.0


def settle(binary, args, keys=(), datadir=None):
    """Runs Canger in a pty and returns (first output, settled) in milliseconds.

    `keys` are sent one at a time after the first frame has settled, and the clock for the
    returned time restarts at the last of them — so a keystroke's cost is measured on its own
    rather than including the startup it followed.
    """
    pid, fd = pty.fork()

    if pid == 0:
        os.environ["TERM"] = "xterm-256color"
        os.environ.setdefault("DOTNET_ROOT", "/opt/anaconda3/envs/dotnet/lib/dotnet")

        # Without a display the ueberzug helper reports itself unavailable and no image process is
        # started, which removes a large non-deterministic term from every reading.
        os.environ.pop("DISPLAY", None)
        os.environ.pop("WAYLAND_DISPLAY", None)
        full = list(args) + (["--datadir", datadir] if datadir else [])
        os.execv(binary, [os.path.basename(binary)] + full)

    # Without this the pty is 0x0 and Canger draws nothing at all.
    fcntl.ioctl(fd, termios.TIOCSWINSZ, struct.pack("HHHH", ROWS, COLS, 0, 0))

    seen = bytearray()
    state = {"require_frame": True}

    def wait_quiet(start):
        first = last = None
        while time.monotonic() - start < DEADLINE:
            ready, _, _ = select.select([fd], [], [], 0.02)
            if ready:
                try:
                    chunk = os.read(fd, 65536)
                except OSError:
                    break
                if not chunk:
                    break
                seen.extend(chunk)
                now = time.monotonic() - start
                first = now if first is None else first
                last = now
            elif last is not None and time.monotonic() - start - last > QUIET:
                # A gap only means "finished" once there is a frame to have finished drawing.
                # Canger emits the alternate-screen escapes first and then goes quiet for as long
                # as the directory takes to scan; on a busy machine with a large directory that
                # gap is longer than the threshold, and stopping there times the escapes rather
                # than the frame.
                if state["require_frame"] and not laid_out(seen.decode("utf-8", "replace")):
                    continue

                break

        return first, last

    # The first wait must see a laid-out frame; a keystroke's wait need not, since the frame is
    # already there and the keystroke may legitimately change nothing.
    started = time.monotonic()
    first, last = wait_quiet(started)
    state["require_frame"] = False

    # If the first frame never settled, a keystroke measured from here would absorb whatever the
    # startup was still doing. Report the failure instead of a fast, meaningless number.
    if keys and last is None:
        try:
            os.kill(pid, 9)
            os.waitpid(pid, 0)
        except OSError:
            pass
        return None, None, ""

    for key in keys:
        os.write(fd, key.encode())
        started = time.monotonic()
        first, last = wait_quiet(started)

    try:
        os.kill(pid, 9)
        os.waitpid(pid, 0)
    except OSError:
        pass

    screen = seen.decode("utf-8", "replace")

    ms = lambda v: None if v is None else v * 1000.0
    return ms(first), ms(last), screen


def laid_out(screen):
    """Whether the captured output is really a laid-out frame.

    A harness that cannot prove it saw a frame will happily time a crash to three decimal places.
    Checking for a border glyph rather than for a filename is deliberate: the title bar is written
    in separately-positioned chunks, so a name can be split across escape sequences and a text
    search gives false negatives — that mistake has been made in this project before.
    """
    return any(glyph in screen for glyph in ("\u2502", "\u250c", "\u2514"))


def case(binary, args, keys=(), runs=5):
    """Runs one case several times, discarding the first, and reports min and median."""
    datadir = tempfile.mkdtemp(prefix="canger-measure-")
    firsts, settles, bad = [], [], 0

    try:
        for attempt in range(runs + 1):
            first, last, screen = settle(binary, args, keys, datadir)

            if attempt == 0:
                continue                    # warm-up, discarded

            if last is None or not laid_out(screen):
                bad += 1
                continue

            firsts.append(first)
            settles.append(last)
    finally:
        shutil.rmtree(datadir, ignore_errors=True)

    if not settles:
        return None

    firsts.sort()
    settles.sort()

    return {
        "paint_min": firsts[0], "paint_med": firsts[len(firsts) // 2],
        "settle_min": settles[0], "settle_med": settles[len(settles) // 2],
        "failed": bad,
    }


def resolve_default():
    """The binary canger.sh would run, by the same order of preference."""
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    rid = os.environ.get("CANGER_RID", "linux-x64")

    for candidate in (
        f"src/Canger.App/bin/Release/net10.0/{rid}/publish/canger",
        "src/Canger.App/bin/Release/net10.0/canger",
        "src/Canger.App/bin/Debug/net10.0/canger",
    ):
        path = os.path.join(root, candidate)
        if os.access(path, os.X_OK):
            return path

    return None


def directories():
    """A small, a medium and a large directory, skipping any that are not present."""
    home = os.path.expanduser("~")
    wanted = [
        ("small", os.path.join(home, "Templates")),
        ("medium", os.path.join(home, "Projects")),
        ("large", os.path.join(
            home, "Productivity_System/02 POOL/01 CREATION ACTIVE/02 RELAX ACTIVE/EVERNOTE")),
    ]

    found = []
    for label, path in wanted:
        if os.path.isdir(path):
            found.append((label, path, len(os.listdir(path))))

    return found


def control(binary, runs=7):
    """How long `--version` takes: argument parsing and nothing else.

    A fixed reference point measured in the same run as everything else. This machine's numbers
    move by a factor of two depending on what else is open — a video player alone did it — and
    without a control there is no way to tell that from a change in Canger. `--version` touches
    none of the code being measured, so any movement in it is the machine, and the ratio between
    a case and this is what can honestly be compared across runs.
    """
    times = []

    for attempt in range(runs + 1):
        started = time.monotonic()
        subprocess.run([binary, "--version"], capture_output=True)

        if attempt:
            times.append((time.monotonic() - started) * 1000.0)

    times.sort()
    return times[len(times) // 2]


def report(binary, targets, previews=False):
    kind = "Debug — do not quote these" if "/bin/Debug/" in binary else "optimised"
    extra = [] if previews else NO_PREVIEW

    print(f"binary   : {binary}")
    print(f"build    : {kind}")
    print(f"previews : {'on' if previews else 'off (so the number is Canger, not bash)'}")
    print()
    reference = control(binary)
    print(f"control  : --version takes {reference:.1f} ms on this machine right now")
    print(f"           (numbers below are also given as multiples of it, which is the only")
    print(f"            figure comparable between runs — see `control` for why)")
    print()
    print(f"{'case':34s} {'first paint':>20s} {'settled':>20s} {'xctl':>7s}")
    print(f"{'':34s} {'min / median':>20s} {'min / median':>20s} {'paint':>7s}")
    print("-" * 84)

    def row(name, result):
        if result is None:
            print(f"{name:34s} {'no valid frame':>20s}")
            return
        note = f"  ({result['failed']} discarded)" if result["failed"] else ""
        print(f"{name:34s} "
              f"{result['paint_min']:7.1f} /{result['paint_med']:7.1f} ms "
              f"{result['settle_min']:7.1f} /{result['settle_med']:7.1f} ms "
              f"{result['paint_min'] / reference:6.1f}x{note}")

    for label, path, count in targets:
        row(f"start in {label} ({count} entries)",
            case(binary, [path] + extra))
        row("  then one cursor move (j)",
            case(binary, [path] + extra, keys=("j",)))


def main():
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--binary", help="the build to measure")
    parser.add_argument("--compare", action="store_true",
                        help="measure the Debug and published builds side by side")
    parser.add_argument("--previews", action="store_true",
                        help="leave previews on; the numbers then include a scope.sh subprocess")
    options = parser.parse_args()

    targets = directories()
    if not targets:
        print("none of the measurement directories exist on this machine", file=sys.stderr)
        return 1

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    if options.compare:
        rid = os.environ.get("CANGER_RID", "linux-x64")
        for candidate in (
            os.path.join(root, "src/Canger.App/bin/Debug/net10.0/canger"),
            os.path.join(root, f"src/Canger.App/bin/Release/net10.0/{rid}/publish/canger"),
        ):
            if os.access(candidate, os.X_OK):
                report(candidate, targets, options.previews)
                print()
            else:
                print(f"not built: {candidate}\n", file=sys.stderr)
        return 0

    binary = options.binary or resolve_default()
    if not binary:
        print("no build found; run ./build.sh publish", file=sys.stderr)
        return 1

    report(binary, targets, options.previews)
    return 0


if __name__ == "__main__":
    sys.exit(main())
