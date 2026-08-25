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

For usage, read the manual: `man canger`, or `canger --man` before it is installed.


Status
------

Canger is usable day to day, and is used that way. Every subsystem of ranger has been ported.

| | |
|---|---|
| Settings | 82, with ranger's global / path-regex / tag scopes |
| Commands | 105 built in, plus whatever `commands.cs` adds |
| Key bindings | 294 in the browser, 36 console, 35 pager, 33 task view |
| Colour contexts | 82, matching ranger's names exactly |
| Colourschemes | `default`, `jungle`, `snow`, `solarized` |
| View modes | miller, multipane |
| VCS backends | git, hg, svn, bzr |
| Image backends | kitty, ueberzug (ranger's other five not yet ported) |
| Tests | 1319 |

Two things go deliberately beyond ranger:

- **Reflink copies.** On btrfs, XFS and bcachefs a same-filesystem copy is a copy-on-write clone
  (`ioctl(FICLONE)`), which is instant and costs no extra space. Failing that it tries
  `copy_file_range(2)`, and failing that a buffered copy.
- **Copy progress with an ETA** — percentage, bytes, live throughput and time remaining. Reflinked
  files are excluded from the throughput estimate, since counting them would make the ETA collapse
  to zero.

Known gaps: five of ranger's eight image protocols (w3m, iterm2, sixel, terminology, urxvt) are
not implemented and fall back to no image; there is no packaging yet. `TODO.md` is the honest
record of what is done, what was measured, and what is known to be missing.


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

```
git clone <this repository> canger
cd canger
./build.sh              # Debug
./build.sh publish      # optimised, ReadyToRun, self-contained layout
./test.sh
```

`build.sh` and `test.sh` export `DOTNET_ROOT`, because the SDK is not always on the default search
path; set `CANGER_DOTNET_ROOT` if yours is somewhere unusual. Two traps that have cost time:

* Without `DOTNET_ROOT`, test apphosts fail to launch and the only symptom is `Zero tests ran`.
* Do **not** pass `--nologo` to `dotnet test`. Under Microsoft.Testing.Platform an unrecognised
  option is forwarded to the test application, which prints its help and runs nothing.

To run without installing, use `./run.sh [path]`.


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

Three flags exist for inspecting what a full-screen interface would hide, and are the quickest way
to answer "why does this not work":

```
canger --config       # what the configuration produced: settings, binding counts, errors
canger --list [path]  # a listing with the configured sort and filters, no terminal takeover
canger --key-probe    # what each key decodes to and which command it resolves to
```


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
tools/              code generators and the measurement harness
doc/                the man page
artifacts/          a real user's ported configuration, compiled by the tests as a fixture
```

`Canger.Core` and `Canger.Tui` are the two foundations and depend on nothing else. Both are fully
testable without a terminal or a real filesystem, through `IFileSystem`, `IProcessRunner` and
friends.

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


About
-----

* **Licence:** GNU General Public License version 3 or later — the same as ranger, of which this
  is a derivative work.
* **Upstream:** ranger, by Roman Zimbelmann and contributors — <https://ranger.fm/>
