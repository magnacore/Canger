Canger
======

Canger is a file manager for the terminal with vi-style key bindings — a port of
[ranger](https://ranger.fm/) from Python to .NET 10 and C#.

It shows a directory as a set of columns: the path leading to where you are, the listing
itself, and a preview of whatever the cursor is on. Moving right enters a directory or opens a
file; moving left goes back up. Almost everything is a command, and almost every key is bound to
one. It ships with `rifle`, ranger's file launcher, which works out which program opens what.

The aim is a **1-to-1 feature match with ranger** — the same key bindings, the same commands, the
same settings and the same configuration file syntax — written as idiomatic C# rather than
transliterated Python. If a key does something in ranger and something else in Canger, that is a
bug; please report it.

For usage, press `?` then `m` inside Canger, which renders the manual for the build you
are running. Outside it: `man canger` once installed, or `canger --man` before that.


Status
------

Canger is usable day to day, and is used that way. Every subsystem of ranger has been ported.

| | |
|---|---|
| Settings | 85 — ranger's 83, plus `unlock_prompt` and `shared_copy_buffer` — with ranger's global / path-regex / tag scopes |
| Commands | 115 built in, plus whatever `commands.cs` adds |
| Key bindings | 295 in the browser, 36 console, 35 pager, 33 task view, 29 devices |
| Colour contexts | 82, matching ranger's names exactly |
| Colourschemes | `default`, `jungle`, `snow`, `solarized` |
| View modes | miller, multipane |
| VCS backends | git, hg, svn, bzr |
| Image backends | kitty, ueberzug (ranger's other five not yet ported) |
| Tests | 1947 |

Nine things go deliberately beyond ranger:

- **Reflink copies.** On btrfs, XFS and bcachefs a same-filesystem copy is a copy-on-write clone
  (`ioctl(FICLONE)`), which is instant and costs no extra space. Failing that it tries
  `copy_file_range(2)`, and failing that a buffered copy.
- **Copy progress with an ETA for the whole queue** — percentage, bytes, live throughput and time
  remaining, covering everything outstanding rather than the file currently moving. Paste two
  films and the bar runs once from nothing to full, instead of restarting when the first
  finishes. Reflinked files are excluded from the throughput estimate, since counting them would
  make the ETA collapse to zero.
- **Removable drives** (`<F9>`, or `:devices`). A list of what is plugged in, with mount, unmount,
  unlock, lock and safely-remove — the last of which unmounts everything on the drive, locks what
  is encrypted, and cuts the power, stopping at the first step that fails. Everything goes through
  `udisksctl`, so a drive mounted here behaves exactly like one mounted from a desktop file
  manager. It can also remember an encrypted drive's passphrase in the desktop's own keyring, the
  one Thunar and GNOME Disks use, so a drive unlocked in either opens in the other. Ranger has no
  equivalent; see **Removable drives** below.
- **It leaves a directory that has been deleted.** Another program removing the folder you are
  standing in used to leave a listing of files that are no longer there, and opening one reported
  that it did not exist. Canger steps up to the nearest directory that is really there and says
  so. Ranger recovers only when asked, with a reset; a drive that has merely stopped answering for
  a moment still moves nobody.
- **It notices changes made outside it.** A `chmod`, `chown` or write from another terminal does
  not touch the directory's modification time, and that is all either program watches — so the
  permissions, owner, size and date on screen stayed as first read, often for a whole session.
  Canger re-reads the rows that are on screen, which costs the same in a directory of twenty
  thousand entries as in one of twenty.
- **Version control that works where ranger's does not.** Ranger runs `chg` unconditionally for
  Mercurial; Canger falls back to `hg`, so it works on a machine that has only Mercurial
  installed. Ranger's Bazaar commit line never appears at all, because its own parser requires a
  newline that it has already stripped. Both programs' Subversion and Bazaar parsers read
  trailing prose as filenames — `svn status` ends with a `Summary of conflicts:` block, and
  `bzr status` describes a conflict rather than naming it — inventing subpaths that cannot exist;
  Canger's reject them, and report a conflicted Bazaar file as conflicted rather than as merely
  modified. `:stage` and `:unstage` are bound to the add and reset that ranger implements in every
  backend and never reaches from a key. All four backends were checked by running ranger's own
  Python and Canger's C# over the same repositories and diffing the results.
- **A copy buffer shared between windows** (`shared_copy_buffer`, off by default). Copy in one
  Canger and paste in another; cut in one and the rows go dim in the other within a redraw.
  Ranger cannot do this at all — its copy buffer is an in-memory set on the file manager. The
  buffer lives beside the bookmarks and tags in `~/.local/share/canger`, outlives the windows the
  way a clipboard does, and is written whole so two windows copying at once end with whichever
  copied last. Off unless asked for, because two Cangers open on unrelated work should not have
  `dd` in one arm `pp` in the other.
- **`rename_stem`.** `cw` clears the whole name, extension and all, so renaming
  `2024-01-07_15-06-24-part-005-019r-021p.mkv` means typing `.mkv` back for no reason; `a` keeps
  the name and leaves the stem to be deleted by hand. `:rename_stem` is the missing third — the
  stem gone, the extension kept, the cursor where the new name starts — and it treats `.tar.gz`
  and its family as one extension. Not bound by default, so the shipped bindings stay ranger's:
  `map cw rename_stem` in your `cc.conf` puts it where it is wanted.
- **Shell commands that behave.** `-q` puts a long command on the task queue instead of freezing
  the interface behind it; `Tab` completes a path and not merely a name in the current directory;
  and a line is run directly when it safely can be, rather than always through `sh -c`, so passing
  two and a half thousand filenames to a program does not fail on Linux's 131 072-byte limit for a
  single argument.

Releases carry a `.deb`, an AppImage and two tarballs:
<https://github.com/magnacore/Canger/releases>. There is no apt repository, so the `.deb` is
installed from the file.

Known gaps: five of ranger's eight image protocols (w3m, iterm2, sixel, terminology, urxvt) are
not implemented and fall back to no image; only `linux-x64` is built, though `CANGER_RID` will
cross-compile. `TODO.md` is the honest record of what is done, what was measured, and what is
known to be missing.


Design goals
------------

* Match ranger's behaviour, and be able to say *where* in ranger's source each behaviour comes from
* Modern C#, not transliterated Python — explicit types, real exceptions, dependency injection
* Extensible in C#: `commands.cs` and `plugins/*.cs` are compiled at startup
* Fast enough that the interface never makes you wait
* Testable without a terminal or a real filesystem


Dependencies
------------

* **.NET 10** runtime. Canger is framework-dependent: the plugin host compiles C# at startup with
  Roslyn, which needs a JIT, so NativeAOT is not an option.
* **Linux.** Canger depends throughout on facilities with no portable equivalent — `statx`,
  `termios`, `SIGWINCH`, `ioctl(FICLONE)` — as ranger effectively does.
* A pager (`less` by default).

### Optional

For general use: `file` for file types, `sudo` for the run-as-root feature, `xclip`/`xsel`/
`wl-copy` for `:yank`.

For previews (through `scope.sh`, whose contract is identical to ranger's, so existing scripts
work unmodified): `ueberzug`, `kitty` or `w3m` for images; `highlight`, `bat` or `pygmentize` for
code; `ffmpegthumbnailer` for video thumbnails; `atool` or `bsdtar` for archives; `pdftotext`,
`mediainfo`, `exiftool`, `odt2txt`, `jq` and the rest of ranger's list.


Building
--------

You need the **.NET 10 SDK** (10.0.302 or newer — `global.json` rolls forward within the feature
band) and a Linux machine. You build from a clone and run it there.

```
git clone https://github.com/magnacore/Canger.git canger
cd canger

./build.sh              # Debug — the development loop
./build.sh Release      # optimised, still JITs itself on the way to the first frame
./build.sh publish      # Release + ReadyToRun, framework-dependent — the one to actually use
./build.sh dist         # tarballs to hand to somebody else
./build.sh deb          # a Debian package
./build.sh appimage     # one executable file
./build.sh release      # check everything, build all four, publish to GitHub
./test.sh               # the whole suite
```

`publish` is the one that matters for speed. ReadyToRun precompiles the IL ahead of time and only
applies at publish, so a plain build — in either configuration — still pays to compile itself on
every launch. It stays framework-dependent (`--self-contained false`), so it uses the runtime
that is already installed rather than bundling one.

`deb` builds `dist/canger_VERSION_amd64.deb`, which is the right answer on Debian or Ubuntu:
`canger` goes on the PATH, the manual where `man canger` finds it, and the shipped `cc.conf`,
`rifle.conf` and `scope.sh` under `/usr/lib/canger/config`. It is self-contained — Debian packages
no .NET runtime at all, so a framework-dependent package would depend on something that does not
exist outside Microsoft's own apt repository.

```
sudo apt install ./dist/canger_0.7.2_amd64.deb
```

`release` is the one that publishes, and it is separate from `dist` on purpose: `dist` is a build
step, run to inspect a package or try a change, and publishing from it would ship whatever happened
to be lying around. `release` refuses unless the working tree is clean, `HEAD` is tagged, the tag
matches `<Version>`, and the tag has not been released already — then it rebuilds all four
artifacts from scratch and checks them before uploading:

```
./build.sh release --dry-run    # every check and the full build, prints the command instead
./build.sh release              # the same, then gh release create
```

Its value is the refusals rather than the upload. Each is a mistake that has happened here: a
`.deb` whose metadata disagreed with the binary inside it, artifacts built at one version while the
tag said another, and a packaged manual describing the maintainer's own key bindings rather than
the defaults. That last check is a comparison against what `canger --clean --man` produces, not a
search for whatever leaked last time.

It needs the [GitHub CLI](https://cli.github.com), logged in with `gh auth login`.

`appimage` writes `dist/Canger-VERSION-x86_64.AppImage` — one already-executable file that needs
nothing installed except FUSE. It bundles the .NET runtime and Canger's own configuration, and
nothing else: `less`, `file`, `git`, `udisksctl` and your editor still come from the machine it
runs on, as they should.

It needs **`fuse3`** on that machine — `fusermount3` and `/dev/fuse` — not the `libfuse2` that
AppImages are famous for wanting; this runtime bundles libfuse statically. `fuse3` is installed by
default nearly everywhere, and where it is not:

```
./Canger-0.7.2-x86_64.AppImage --appimage-extract-and-run
```

Building one needs `appimagetool`, which Debian does not package — take the `x86_64` build from
[the AppImage project](https://github.com/AppImage/appimagetool/releases) and put it on your PATH.

`dist` is for giving Canger to someone else. It writes two lzip tarballs into `dist/` — unpack
with `tar --lzip -xf`, which needs `lzip` installed — a
framework-dependent one for a machine that already has .NET 10, and a self-contained one that
needs nothing installed at all. Both are ReadyToRun, both leave out the debug symbols, the API
documentation and the Roslyn translations that `publish` keeps, and both carry `config/` — without
which Canger starts with no key bindings whatsoever — along with `LICENSE`, this file and the man
page. Each is started once before it is packaged, because a publish that emits a broken assembly
still reports success.

It publishes for `linux-x64`; set `CANGER_RID=linux-arm64` if that is what you are on.

### Finding the SDK

Every script sets `DOTNET_ROOT` before doing anything, because a .NET apphost cannot find a
runtime without it and the error it gives when it cannot does not say so. They look in three
places, in order: `CANGER_DOTNET_ROOT`, then the path the SDK happens to live at on the machine
Canger was written on, then whatever `dotnet` is on your `PATH` — which is the one that will find
it for you. Failing all three you get a sentence saying so rather than a missing-file error.

If yours is somewhere none of those reach:

```
export CANGER_DOTNET_ROOT=/usr/share/dotnet
```

Two traps that have cost time here:

* Without `DOTNET_ROOT`, test apphosts fail to launch and the only symptom is `Zero tests ran`.
* Do **not** pass `--nologo` to `dotnet test`. Under Microsoft.Testing.Platform an unrecognised
  option is forwarded to the test application, which prints its help and runs nothing.


Running it
----------

Three ways in, for three different purposes.

```
./run.sh [path]         # development: rebuilds Debug and runs it
./canger.sh [path]      # normal use: runs the fastest build that exists
src/Canger.App/bin/Release/net10.0/linux-x64/publish/canger [path]
```

**`run.sh`** is the edit-compile-run loop. It rebuilds quietly first and deliberately runs the
**Debug** build, because a fast rebuild matters more there than a fast start. `CANGER_NO_BUILD=1`
skips the build. Do not measure anything with it.

**`canger.sh`** is the one to use day to day, and the one to put on your `PATH`. It picks the
fastest build present — published, then Release, then Debug — and says so on stderr when it has
had to fall back to Debug. It also sets the handful of variables Canger's own commands need
(`PATH` including `~/.local/bin`, `VISUAL`, `EDITOR`, `TERMINFO`), which matters when Canger is
started from a window-manager keybinding rather than a shell, where it would otherwise inherit
almost nothing. `CANGER_BINARY` overrides which build it runs.

To have `canger` as a command:

```
./build.sh publish
mkdir -p ~/.local/bin
ln -s "$PWD/canger.sh" ~/.local/bin/canger
```

The symlink is resolved before the build is looked for, so it finds the right one wherever the
link lives.

**Changing something and seeing it.** Editing Canger's own source means rebuilding — `./build.sh
publish` — and the next launch has it. Editing your *configuration* (`cc.conf`, `commands.cs`,
`rifle.conf`, `scope.sh`, `plugins/`) needs no build at all: `commands.cs` and `plugins/*.cs` are
compiled by Canger at startup, so restarting is enough.


Getting started
---------------

Navigate with the arrow keys or `h` `j` `k` `l`, `Enter` to open, `q` to close a tab or quit.
`?` opens the manual.

Canger reads its configuration from `~/.config/canger`, and writes state (bookmarks, tags,
history) to `~/.local/share/canger`. Copy the shipped defaults to start from:

```
canger --copy-config=all      # or: cc, rifle, commands, scope
```

| File | ranger's equivalent | what it does |
|---|---|---|
| `cc.conf` | `rc.conf` | settings and key bindings, same syntax line for line |
| `commands.cs` | `commands.py` | your own commands, in C#, compiled at startup |
| `rifle.conf` | `rifle.conf` | which program opens which file — identical syntax |
| `scope.sh` | `scope.sh` | preview generation — identical contract |
| `plugins/*.cs` | `plugins/*.py` | commands, linemodes and hooks |
| `colorschemes/*.cs` | `colorschemes/*.py` | colours |

### Plugins compile themselves

`commands.cs` and every `plugins/*.cs` are C#, and **Canger compiles them itself when it starts**.
You never build a plugin by hand, and no SDK is needed to write one — edit the file, restart
Canger, and it is live. Change an icon in `plugins/devicons.cs` and the next launch has it; drop a
new `.cs` into `plugins/` and it is found without being registered anywhere.

The build is cached under `~/.cache/canger/plugins`, keyed by a hash of the file names and their
contents, so an unchanged configuration costs nothing at startup and an edited one is rebuilt once.
There is no hot reload: editing a plugin while Canger is running does nothing until you restart it.

Two things worth knowing before you edit one:

- **Every `.cs` in `plugins/` is compiled as a single assembly**, in filename order — so a plugin
  can rely on naming to load after another, and **a syntax error in any one of them stops them all
  from loading**, not just the file at fault. Fumble a brace in `devicons.cs` and your archive and
  zoxide commands disappear with the icons. `commands.cs` is compiled separately, so a mistake
  there does not take the plugins down with it.
- **`canger --config` tells you why.** It reports each compilation and, when one fails, prints the
  compiler's own diagnostics with file, line and column:

  ```
  plugin plugins-ba34a68f: did not compile
    …/broken.cs(2,1): error CS1002: ; expected
  ```

  Run it after editing a plugin; a plugin that failed to compile is otherwise silent.

A filename beginning with `_` is skipped, which disables a plugin without deleting it. A prebuilt
`.dll` dropped into `plugins/` is loaded directly, with no compilation.

Three flags exist for inspecting what a full-screen interface would hide, and are the quickest way
to answer "why does this not work":

```
canger --config       # what the configuration produced: settings, binding counts, errors
canger --list [path]  # a listing with the configured sort and filters, no terminal takeover
canger --key-probe    # what each key decodes to and which command it resolves to
```


Removable drives
----------------

Ranger has nothing of this kind; it is Canger's own. Press **`<F9>`** — or type `:devices` — for a
list of what is plugged in:

```
Devices
  My Passport       4 T  LUKS      unlocked                  (WDC WD40NMZW-59GX6S1)
  BACKUP_01_A       4 T  ext4      /media/manuj/BACKUP_01_A   (WDC WD40NMZW-59GX6S1)

  <CR> mount and enter   m mount   u unmount   l unlock   L lock   e eject   r reload   q close
```

`<CR>` does what clicking a drive does in a desktop file manager: mounts it if it needs mounting,
unlocking it first if it is encrypted, and then goes there. The list re-reads itself every two
seconds while it is open, so a drive plugged in appears on its own. Its keys are bound with
`dmap`, the same way the task view's are bound with `tmap`.

**`e` is safely remove**: unmount every filesystem on the drive, lock every encrypted container on
it, then power the drive off. The steps are joined with `&&`, so it stops at the first one that
fails — a filesystem that will not unmount means the power is never cut.

Everything runs through **`udisksctl`** and nothing else: no `mount(8)`, no `umount`, no `eject`,
nothing as root. That is what makes a drive mounted here behave exactly like one mounted from
Thunar or Nautilus — same `/media/$USER` location, same polkit rules. Unlocking is the one action
given the terminal, because `udisksctl` prompts for the passphrase itself with the echo off: the
passphrase goes from your keyboard to udisks without passing through Canger.

Four things it will refuse to do:

* **Force anything.** There is no `-f` and no lazy unmount anywhere. A busy filesystem fails and
  says so.
* **Act on a stale row.** The list is read again before every action, because what is on screen
  can be two seconds old.
* **Show, or touch, anything holding `/`, `/boot`, `/home` or swap** — even a drive that reports
  itself hot-pluggable, which internal hot-swap bays and eSATA do.
* **Unmount a drive Canger is itself copying to or from.** The kernel refuses while a file is
  open, but a transfer between two files holds nothing, and an unmount landing in that gap would
  break the copy.

Unmount and eject step off the drive first — every tab looking at it goes home and its cached
listings are dropped — since a file manager showing a directory is a reason that directory cannot
be unmounted.

**No `sync` is needed and none is called.** The unmount is the flush and blocks on it: measured
here, 512 MB written with no sync leaves 525 MB dirty, and `udisksctl unmount` takes 0.54 s and
leaves none, against 0.07 s for the same unmount with nothing outstanding. `sync(1)` is global, so
adding one would make ejecting a memory stick wait on dirty data belonging to every other
filesystem.

### Remembering a passphrase

By default an encrypted drive is unlocked the way it always was: `udisksctl` is given the screen
and prompts for the passphrase itself, nothing of it passes through Canger, and nothing is kept.

`set unlock_prompt builtin` moves the prompt inside Canger, drawn as bullets, and three places
are then tried in turn before you are asked — what you chose to keep for this sitting, what the
desktop's keyring holds, and only then the keyboard. After a passphrase works you are offered
**never**, **this session**, or **the keyring**.

The keyring is the desktop's, not Canger's. Thunar, Nautilus and GNOME Disks all file a LUKS
passphrase through libsecret under `org.gnome.GVfs.Luks.Password`, keyed by the volume's LUKS
UUID, and Canger uses exactly that:

```
gvfs-luks-uuid : 61858679-035e-4001-94c3-0e6946fc85df
xdg:schema     : org.gnome.GVfs.Luks.Password
label          : Encryption passphrase for WDC WD40NMZW-59GX6S1 (4.0 TB Hard Disk)
```

So **a passphrase saved in Thunar unlocks the drive in Canger without being typed again**, and one
saved here works in Thunar. There is one entry per drive, and it is visible and removable in
Seahorse like any other.

Where the passphrase goes, and does not:

* To udisks down a **pipe** — `udisksctl unlock --key-file /dev/stdin` — so it is never written to
  a file and never appears in a command line where `ps` would show it.
* To libsecret on **`secret-tool`'s standard input**, for the same reason.
* **Not** into the command history, which is written to disc; not into a completion; and an Up
  arrow at a passphrase prompt recalls nothing.

`:forget_passphrases` drops whatever is being kept in memory without touching the keyring, for
when you are about to leave the terminal.

The keyring half needs **libsecret-tools** (`secret-tool`) installed. Without it the "keyring"
option is not offered and the other two still work.

Needs **udisks2** installed. Without it the list says so and does nothing else.


Contributing
------------

Bug reports are most useful when they say what you pressed, what happened, and what ranger does
instead. "Ranger does X" is a complete argument here — matching it is the whole point.

### Repository layout

```
src/
  Canger.Core/      filesystem model, settings, tabs, commands, macros, task queue, file operations
  Canger.Tui/       raw mode, screen buffer, input decoder, key-binding trie, wide characters
  Canger.Ui/        widgets, views, colourschemes
  Canger.Rifle/     the rifle.conf engine
  Canger.RifleCli/  the standalone `rifle` command
  Canger.Preview/   scope.sh runner, preview cache, image backends
  Canger.Vcs/       git, hg, svn, bzr
  Canger.Plugins/   the Roslyn compile host
  Canger.App/       entry point, command-line parsing, composition root
tests/              one test project per source project, plus Canger.TestSupport
config/             the shipped cc.conf, rifle.conf, scope.sh, commands.cs
tools/              code generators, and `screen.py` for driving Canger in a real terminal
doc/                the icon; the man page is generated, never stored
artifacts/          a real user's ported configuration, compiled by the tests as a fixture
```

`Canger.Core` and `Canger.Tui` are the two foundations and depend on nothing else. Both are fully
testable without a terminal or a real filesystem, through `IFileSystem`, `IProcessRunner` and
friends.

Some behaviour only exists on a screen, and the test suite cannot see it — a column's layout, a
colour, what a key actually does. `tools/screen.py` runs Canger in a pty and rebuilds the screen
from the escape stream, so it can be read back as rows of text with the colour of every cell. It
is documented, with the five ways it will mislead you, in `.claude/skills/pty-verify`.
`.claude/skills/ranger-parity-check` records the other method that keeps paying: settle a "does
this match ranger?" question by running ranger's own class and Canger's over the same input and
diffing, rather than by reading either source.

### The reference implementation

Ranger's Python source is the authority on behaviour. It is not vendored here — clone it beside
this repository if you are porting or verifying something:

```
git clone https://github.com/ranger/ranger.git ranger-master
```

A fresh clone tests green with **five skips**, all expected, for three reasons:

* two tests parse a real user's `rc.conf` from `../ranger-settings/rc.conf` as a compatibility
  fixture — put one there and they run;
* one checks the settings catalogue against `../ranger-master/ranger/container/settings.py` — the
  clone above enables it;
* two exercise reflink copies and skip on a filesystem that has no `FICLONE`. They run on btrfs,
  XFS and bcachefs; if your temporary directory is `tmpfs`, they will not.

A skip predicated on a path is a test that deletes itself when the path moves, which has already
happened once here — if you move a fixture, check the skip count.

### Conventions

These are enforced by the build (`TreatWarningsAsErrors`) or by review:

* **No `var`.** Explicit types everywhere.
* **Document why, not what.** A comment that restates the code earns nothing. A comment saying
  which ranger behaviour a line reproduces, and where it lives in ranger's source, earns a lot —
  most of the code here carries a `container/directory.py:428`-style reference, and they are worth
  keeping accurate.
* **Test the drawn result, not the inputs.** The most common defect in this port has been
  *"the styling exists and nothing produces the input"* — tag markers, colour contexts, cumulative
  sizes and `dirname_in_tabs` all shipped with everything present except the one line that fed
  them, and every unit test passed throughout, because they asserted names and text rather than
  attributes and rendered output.
* **Measure before optimising, and record what you measured** — including what you tried and
  rejected. `TODO.md` has a running list of both; parallelising the directory scan, for instance,
  measured *worse* than serial at every degree tried.

### Branching

Canger uses git-flow.

| Branch | Purpose |
|---|---|
| `main` | released, tagged versions only |
| `develop` | integration; branch from here and merge back here |
| `feature/*` | new work, from `develop` |
| `release/*` | stabilising a version, from `develop`, merged to both |
| `hotfix/*` | urgent fixes, from `main`, merged to both |

```
git switch develop
git switch -c feature/what-it-does
# ... work, with tests ...
git switch develop && git merge --no-ff feature/what-it-does
```

Keep `./test.sh` green in every commit that lands on `develop`.

**Pull requests go to `develop`, not `main`.** `main` is the default branch here because it is what
someone arriving to *use* Canger should land on — the released, tagged state — but under git-flow
work starts from `develop` and merges back there. `main` only moves when a release does.

**Cutting a release.** Bump `<Version>` in `Directory.Build.props` on a `release/*` branch, merge
it to `main` and to `develop`, tag `main` with `vVERSION` — the tag message becomes the release
notes — and then:

```
./build.sh release --dry-run    # see that everything agrees
./build.sh release              # build, check, publish
```


About
-----

* **Licence:** GNU General Public License version 3 or later — the same as ranger, of which this
  is a derivative work.
* **Upstream:** ranger, by Roman Zimbelmann and contributors — <https://ranger.fm/>
