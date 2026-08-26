# Canger — port status

Canger is a 1-1 C# / .NET 10 port of [ranger](https://ranger.fm), the Python TUI file manager.
The Python source under `../ranger-master/` is the reference implementation and the authority on
behaviour; `../ranger_settings/` is a real user configuration that must parse unchanged.

This file is the resume point between sessions. Update it at the end of every phase.

## Build and test

```bash
./build.sh          # dotnet build Canger.slnx
./test.sh           # dotnet test Canger.slnx
```

Both scripts export `DOTNET_ROOT`, which is required here because the SDK lives at
`/opt/anaconda3/envs/dotnet/lib/dotnet`, outside the default search path. Two traps worth
knowing, both of which cost time once already:

- Without `DOTNET_ROOT`, test apphosts fail to launch and the only symptom is `Zero tests ran`.
- Do **not** pass `--nologo` to `dotnet test`. Under Microsoft.Testing.Platform, unrecognised
  options are forwarded to the test application, which prints its help and runs nothing.

## Running it

```bash
export DOTNET_ROOT=/opt/anaconda3/envs/dotnet/lib/dotnet
src/Canger.App/bin/Debug/net10.0/canger [path]     # the browser; q quits
```

Three modes exist for inspecting things a full-screen interface would hide:

```bash
canger --config       # what the configuration produced: settings, binding counts, errors
canger --list [path]  # a listing with the configured sort and filters, no terminal takeover
canger --key-probe    # what each key decodes to and which command it resolves to; Ctrl-Q quits
```

`--key-probe` is the quickest way to answer "why does this key not work".

## Progress

- [x] **Phase 0 — Scaffold**
- [x] **Phase 1 — Settings + cc.conf**
- [x] **Phase 2 — TUI foundation**
- [x] **Phase 3 — Filesystem model**
- [x] **Phase 4 — Miller view + keybindings** ← Canger runs
- [x] **Phase 5 — Console + command system**
- [x] **Phase 6 — Rifle + process runner**
- [x] **Phase 7 — Copy engine (reflink + ETA)**
- [x] **Phase 8 — Previews (scope.sh, pager, image protocols)**
- [x] **Phase 9 — Bookmarks, tags, task view, linemodes, colourschemes, multipane, tabs**
- [x] **Phase 10 — eval, plugin host, VCS, CLI flags, man page** ← the port is complete

---

## Phase 0 — Scaffold ✅

Solution of 8 libraries and 4 test projects, quality gates on, native filesystem layer working.

Done:

- `Canger.slnx` (the .NET 10 XML solution format), `Directory.Build.props`,
  `Directory.Packages.props` (central package management), `global.json`.
- Warnings are errors; analysers at `latest-recommended`; XML docs required in `src/`.
- `Canger.Core/Native/` — `statx`, `statvfs`, `ioctl`, `copy_file_range`, `link`, and NSS name
  lookups, all via source-generated `LibraryImport`.
- `Canger.Core/FileSystem/` — `IFileSystem`, `FileStatus`, `FileKind`, `DirectoryEntry`,
  `LocalFileSystem`, `UserDatabase`.
- `config/` — `cc.conf`, `rifle.conf`, `scope.sh`, `mime.types`, sample `commands.cs`.
- 26 tests passing.

Verified against the system: listing, sizes, owners, link counts, inodes and free space all
agree with `ls -la` and `df`.

### Decisions worth remembering

- **`statx`, not `stat`.** glibc versions the `stat` symbol; `statx` has had one fixed 256-byte
  layout since Linux 4.11. It also splits the device into major/minor, which the reflink
  same-filesystem check needs.
- **Project references invert the plan's diagram.** `Canger.Core` depends on *nothing*.
  `Rifle`, `Vcs`, `Preview`, `Ui` and `Plugins` depend on `Core`; `App` composes them. The plan
  showed conceptual usage; real project references point the other way so the domain stays
  dependency-free and testable.
- **`AllowUnsafeBlocks` only in `Canger.Core`**, because the `LibraryImport` generator emits
  unsafe marshalling. No hand-written unsafe code exists.
- **CA1859 is suppressed solution-wide.** It demands concrete types over interfaces for
  performance, which is in direct conflict with programming against `IFileSystem`. Rejected
  deliberately; the rationale is in `Directory.Build.props`.
- **No NativeAOT**, ever: Roslyn plugin compilation needs a JIT.
- **C# has no octal literals.** Unix modes are written as hex with the octal in a comment.

### Known placeholders to remove

- `src/Canger.{Tui,Ui,Rifle,Preview,Vcs,Plugins}/AssemblyInfo.cs` — empty files so the projects
  compile before their first real type.
- `tests/Canger.{Tui,Ui,Rifle}.Tests/PlaceholderTests.cs` — Microsoft.Testing.Platform reports a
  project with zero tests as a failure.
- `src/Canger.App/Program.cs` — a directory-listing smoke test, not the real composition root.
- `config/cc.conf` etc. are rebranded copies of ranger's defaults. The keybindings and settings
  are verbatim on purpose; only names and paths were changed.

---

## Phase 1 — Settings + cc.conf ✅

All 83 settings, three scopes, the change pipeline, and the cc.conf reader. Still headless.
85 tests passing.

Done:

- `Signals/` — `SignalBus` with priority-ordered handlers and disposable subscriptions.
- `Settings/` — `SettingsCatalog` (83 definitions), `SettingsStore` (global, path and tag
  scopes), `SettingValueParser`, `SettingChange`, and `CangerSettings`, a generated typed facade.
- `Configuration/` — `CangerPaths` (XDG layout), `ConfigurationReader` (layered, additive,
  error-tolerant), `IConfigurationDirective`, `SetDirective`.
- `config/` is deployed beside the executable, so Canger is fully configured with no user config.

Verified end to end: loading the user's real `ranger_settings/rc.conf` applies their custom
`hidden_filter`, `preview_images true` and `ueberzug` method with **zero** setting errors — only
the directives later phases will register are reported as unhandled.

### Decisions worth remembering

- **83 settings, not 84.** A test parses ranger's `ALLOWED_SETTINGS` directly and asserts exact
  parity in both directions, so the catalog cannot drift. Only `nested_ranger_warning` is renamed,
  to `nested_canger_warning`, because it names the program.
- **A setting change is one signal announced under two names.** `SignalBus.EmitTo` merges the
  handlers of `setopt` and `setopt.<name>` into a single priority-ordered pass. Emitting the two
  names in sequence looks equivalent but is not: every handler of the first name, including the
  commit, would run before any handler of the second name could sanitise anything. This was a
  real bug, caught by a test, and is why `EmitTo` exists at all.
- **`setinpath` anchors at the END**, not the start: ranger formats it as
  `re.escape(path) + "$"` (`ranger/config/commands.py:568`), so `path=build` matches a build
  directory anywhere in the tree and `path=/data` does *not* cover `/data/photos`. Getting this
  backwards is easy and silently changes which directories a scope applies to.
- **Scope operands may be quoted** with either quote style and may contain spaces and escaped
  quotes. The user's configuration relies on this heavily — directory names like
  `04 BIN/04 RA RP SP` are everywhere in it. `setinpath` also accepts `pattern=`, and
  `setinregex` accepts `re=`, `regex=` and `pattern=`.
- **Canger ships real defaults in code**, unlike ranger, which relies on rc.conf always loading.
  `SettingsCatalogTests.ShippedConfigAgreesWithTheCatalogDefaults` pins the two together.
- **Parsing is typed, not guessed.** Ranger tries int, then bool, then float, then string
  (`core/actions.py:113-137`), so `set scroll_offset banana` silently stores a string. Canger
  parses against the declared kind and reports the error.
- Analyser suppressions added, each with a rationale in `Directory.Build.props`: CA1716 and
  CA1720 (cross-language interop rules that reject `Get`, `Set` and `SettingKind.Integer`).

---

## Phase 2 — TUI foundation ✅

Key handling, character widths, the screen buffer, input decoding and the terminal itself.
303 tests passing, and the whole input path verified against a real pty.

Done:

- `Canger.Core/Input/` — `SpecialKeys.g.cs` (generated), `KeyCodes`, `KeyBindingParser`,
  `KeyMapNode`, `KeyMap`, `KeyMaps`, `KeyBuffer`, `Direction`.
- `Canger.Core/Configuration/KeyBindingDirective` — the `map`, `copymap` and `unmap` families
  across all four contexts, plus the deprecated `cunmap` / `punmap` / `tunmap` spellings.
- `Canger.Tui/Text/` — `EastAsianWidth.g.cs` (generated), `CellWidth`, `WideString`.
- `Canger.Tui/Rendering/` — `Color`, `CellStyle`, `CellAttributes`, `Cell`, `ScreenBuffer`, `Ansi`.
- `Canger.Tui/Input/` — `InputEvent` hierarchy, `InputDecoder`.
- `Canger.Tui/` — `Terminal` (raw mode, alternate screen, mouse, bracketed paste, SIGWINCH, poll).
- `Canger.App/KeyProbe` — a diagnostic mode, `canger --key-probe`, that shows what each key
  decodes to and which command it resolves to.

Verified: 338 bindings load from the shipped cc.conf, and driving a real pty confirms `3gg` →
`move to=0` with the count, `<up>` → `move up=1`, `<f1>` → `help`, `<A-j>` → `scroll_preview 1`,
SGR mouse, bracketed paste, `dd` → `cut`, bare Escape → `change_mode normal`, and a clean exit
that restores the cursor, the alternate screen, mouse reporting and bracketed paste.

### Decisions worth remembering

- **Two tables are generated from ranger, not transcribed** — `tools/generate-special-keys.py`
  and `tools/generate-east-asian-width.py`. Regenerate rather than hand-edit. Hand transcription
  would have got at least three entries wrong: `c-_` is **-1** (`ord('_') - 96` underflows), and
  key codes 9 and 10 render as `c-i` and `c-j` rather than `tab` and `return`, because the
  control-key entries are defined after the base table and overwrite it.
- **Key codes below 256 are input bytes, not Unicode code points.** A multi-byte character
  arrives as several key events. That is what lets a binding for a non-ASCII key match the bytes
  the key actually produces; reassembling text is the console's job in a later phase.
- **`Canger.Tui` depends on `Canger.Core`**, for the shared key vocabulary. Core still depends on
  nothing, so the stack is linear: Core ← Tui ← Ui.
- **Do not use `System.Console` for terminal I/O.** It reconfigures the terminal driver for its
  own key handling the first time it is used, silently undoing raw mode — input kept being echoed
  and never reached the application. `Terminal` opens descriptors 0 and 1 directly instead.
- **`default(Color)` had to mean "terminal default", not black.** Styles are routinely passed as
  `default`, and a struct's default is all zero bits, so index 0 would have painted everything
  black on black. `Color` stores index + 1 privately to make the zero value correct.
- **A lone escape byte is ambiguous** and can only be resolved by waiting. `Terminal.WaitForInput`
  wraps `poll(2)`; the loop passes a 25 ms timeout while the decoder is holding something and
  blocks indefinitely otherwise. Without it, Escape followed by any key reads as an Alt
  combination — which is exactly what happened before the timeout was added.
- **`WideString` deviates from ranger in one undocumented edge case.** Ranger drops a character
  when a slice starts mid-wide-character and ends on a boundary (`ext/widestring.py:137-138`),
  leaving a one-cell hole. Canger returns the full slice, so a slice always fills the cells it
  was asked for. Every case ranger documents behaves identically.
- **Analyser suppressions:** none added this phase. `SupportedOSPlatform("linux")` is declared
  once for the whole solution in `Directory.Build.props`, which is accurate and lets the
  analysers verify the P/Invoke calls rather than flag them.

### Known placeholders to remove

- `src/Canger.{Ui,Rifle,Preview,Vcs,Plugins}/AssemblyInfo.cs` and
  `tests/Canger.{Ui,Rifle}.Tests/PlaceholderTests.cs` — still empty projects.
- `alias` and `eval` remain the only unhandled cc.conf directives; both belong to the command
  system in Phase 5.

---

## Phase 3 — Filesystem model ✅

The file list: loading, sorting, filtering, flat mode, marks, tabs, history and the task queue.
415 tests passing, all of the domain against an in-memory filesystem.

Done:

- `tests/Canger.TestSupport/InMemoryFileSystem` — a full `IFileSystem` in memory, modelling file
  kinds, sizes, timestamps, permissions, devices, and symbolic links including broken ones. The
  whole domain is tested through it, so no test needs a temporary directory.
- `Canger.Core/Model/` — `FsNode`, `FileNode`, `DirectoryNode`, `NaturalSortKey`, `SortOrder`,
  `NodeSorter`, `Cursor`, `DirectoryCache`, `History<T>`, `Tab`.
- `Canger.Core/Model/Filters/` — `IFileFilter`, name/type/narrow/predicate filters, the
  and/or/not combinators, and `FilterStack` with its reverse Polish behaviour.
- `Canger.Core/Tasks/` — `ILoadable`, `QueuedTask`, `TaskQueue`.

Verified end to end against the real filesystem: the app lists `ranger-master` with the
configured sort order and hidden filter, shows 14 of 18 entries by default, and reveals the four
dotfiles and re-sorts by size when a cc.conf asks it to.

### Decisions worth remembering

- **`SortOrder` is a record *class*, not a record struct.** A struct's parameterless construction
  zero-initialises and skips the primary constructor's default values, so `new SortOrder()` meant
  "files before directories, case sensitive" — the opposite of the intent, and invisible until a
  listing came back wrong. `SortOrderTests.DefaultConstruction_AppliesTheIntendedDefaults` pins
  it. This is the same trap `Color` hit in Phase 2; treat default-value semantics on any new
  options struct as suspect.
- **Sorting is one composite comparison, not two passes.** Ranger sorts by name and then by
  directory-ness, relying on Python's stable sort (`container/directory.py:529-563`).
  `List<T>.Sort` is an introsort and is *not* stable, so that approach scrambles names within
  each group. Canger sorts by key with a name tie-break, then partitions directories out
  afterwards — which also keeps the grouping upright when the order is reversed.
- **Marks live on the node, not the directory**, so they survive re-sorting and re-filtering for
  free. A rescan builds fresh nodes, so the directory records marked paths beforehand and
  restores them afterwards.
- **`Selection` is "the marked entries, or the one under the cursor".** That single rule is why
  `dd` needs no separate variant for one file and many.
- **Flat depth is `depth < level`, not `depth + 1 < level`.** Level 1 means "fold in one level
  below this directory". The hidden pattern is matched against *every* path component, which only
  matters in flat mode but is what stops a flattened listing filling with dot-directories.
- **Tabs keep their own cursor** separate from the shared directory's, because directories are
  interned and two tabs on the same directory would otherwise fight over one position.
  `Tab.Activate` writes the tab's remembered position back before use.
- **Task steps are `IEnumerator<Unit>`, deliberately not `async`.** What matters is the strict
  ordering, the ability to stop between steps, and that only the front job advances. Parallelism
  belongs inside a step. `TaskQueue.Work(TimeSpan.Zero)` advances exactly one step, which keeps
  tests deterministic instead of timing-dependent.
- **Analyser suppressions added:** `CA1711` at the declarations of `FilterStack` and `TaskQueue`
  (both match user-facing vocabulary), and a scoped `CA1309` in `NodeSorter` where the
  culture-sensitive comparison is exactly what `sort_unicode` asks for.

### Known placeholders to remove

- `src/Canger.{Ui,Rifle,Preview,Vcs,Plugins}/AssemblyInfo.cs` and
  `tests/Canger.{Ui,Rifle}.Tests/PlaceholderTests.cs` — still empty projects.
- `alias` and `eval` remain the only unhandled cc.conf directives, both Phase 5.
- Not yet ported from ranger's model, because nothing needs them until later phases: mimetype
  classification beyond the extension, the hash/duplicate/unique filters (they need content
  hashing, which belongs with the copy engine), cumulative directory sizes, and VCS status.

---

## Phase 4 — Miller view + keybindings ✅

**Canger runs.** It launches, draws the miller columns, navigates with ranger's keys, and quits.
463 tests passing.

Done:

- `Canger.Ui/Styling/` — `ContextKey.g.cs` (generated, 82 keys), `StyleContext`, `ColorScheme`,
  `DefaultColorScheme` ported from ranger's `colorschemes/default.py`.
- `Canger.Ui/Widgets/` — `Widget`, `Rect`, `BrowserColumn`, `TitleBar`, `StatusBar`.
- `Canger.Ui/Views/MillerView` — the column layout.
- `Canger.Ui/Browser` — the main loop: wait, decode, match, execute, advance tasks, redraw.
- `Canger.App/Program` — the composition root plus the `--config`, `--list` and `--key-probe`
  diagnostic modes.

Verified by driving a real pty: three columns render with the ancestry on the left and a preview
of the selected directory on the right; `j`/`k`/`G`/`3j` move the cursor; `l` and `h` enter and
leave directories with the pathway and breadcrumb cursors following; the status bar shows
permissions, ownership, size, time, free space and position; and `q` exits cleanly with the
terminal restored.

### Decisions worth remembering

- **`StyleContext` is a bitset in two words, not a set object.** One is built per row per frame
  and used as the colourscheme cache key, so it has to be free to build, compare and hash. 82
  keys fit in 128 bits with room to spare.
- **Colourschemes are cached by context set.** A session produces a few dozen distinct sets, so
  after the first frame every lookup is a dictionary hit. Derive from `ColorScheme`, not
  `IColorScheme`, to get this.
- **Column widths: each column gets `share - 1` cells while the cursor advances a full `share`.**
  That one-cell difference *is* the gutter. The last column takes the remainder so the right edge
  does not shift as the terminal resizes. Tests assert non-overlap at several widths.
- **Only the main column shows sizes.** The ancestry columns are narrow; spending their width on
  a size left six characters for names, which was visibly wrong when first run.
- **An unscanned directory shows no count rather than a placeholder.** Scanning every directory
  in view just to print a count would make entering a large tree crawl.
- **The scroll rule needs a fallback for short columns.** When the margin cannot be honoured on
  both sides — any column shorter than twice `scroll_offset` — the cursor is centred instead.
- **`TextAt` returns characters, not cells.** A row of wide characters is shorter as a string
  than the column is wide. Assert with `CellWidth.Of(...)`, not `.Length`; the first version of
  the wide-character test got this wrong.

### Known gaps

- `Browser.Execute` handles a navigation subset only: `quit`, `move`, `cd`, `history_go`,
  `move_parent`, `mark_files`, `set`, `reload_cwd`, `redraw_window`. Anything else reports
  "not yet implemented" in the status bar. The real command system is Phase 5.
- No preview of *files* yet — the third column shows a directory listing when a directory is
  selected and is otherwise blank. Previews are Phase 8.
- No console, no tabs beyond the first, no multipane view, no borders, no line numbers.
- `Browser` itself has no unit tests: it needs a real terminal. It is covered by driving a pty,
  which is how every defect found in this phase was found.
- Placeholders still present: `src/Canger.{Rifle,Preview,Vcs,Plugins}/AssemblyInfo.cs` and
  `tests/Canger.Rifle.Tests/PlaceholderTests.cs`.

---

## Phase 5 — Console + command system ✅

Commands are real objects with completion, live preview and macro expansion, and the `:` prompt
works. 563 tests passing.

Done:

- `Canger.Core/Commands/` — `IFileManager` (the surface commands act on), `CangerCommand`,
  `[Command]`, `CommandLine`, `CommandRegistry`, `CommandDispatcher`, `MacroExpander`.
- `Canger.Core/Commands/Builtin/` — 49 commands: navigation, `cd`, `set`, `console`, `chain`,
  `scout` and its filter family, `mkdir`, `touch`, `rename`, `copy`/`cut`/`uncut`, `flat`,
  `filter_stack`, `yank`, `quit` and friends.
- `Canger.Core/Configuration/AliasDirective` — `alias`, which clears one of the two remaining
  cc.conf directives. Every alias in the shipped configuration now resolves.
- `Canger.Ui/Widgets/ConsoleWidget` — line editing, word movement, kill and yank, prefix-filtered
  history, completion cycling, question mode, and multi-byte character reassembly.
- `Browser` now implements `IFileManager` and dispatches through the registry; the switch
  statement is gone.
- `tests/Canger.TestSupport/FakeFileManager` — commands are tested against this, with no terminal.

Verified through a pty: `:` opens the prompt, `:cd /tmp` moves, `:set show_hidden true` and the
`zh` binding both reveal dotfiles, `:qui` + Tab completes to `:quit`, and Enter runs the line.

### Decisions worth remembering

- **Resolve the command before expanding macros.** Whether macros expand, and whether they are
  shell-quoted, are properties of the command, not of the line. Expanding first would apply the
  wrong rules and would expand `:map`'s argument, which must stay untouched until the key it
  binds is pressed.
- **Shell quoting is per value, not per expansion.** Joining the selection and quoting the result
  would turn several files into one argument. A file named `; rm -rf ~` is a test case, and it
  passes.
- **A macro with no value aborts the line.** `%c` with an empty clipboard expands to nothing,
  which would turn `shell rm -rf %c` into `rm -rf`. It raises instead.
- **`Initialize` had to be virtual.** `AliasedCommand` overrides it to substitute the expanded
  line; with `new` instead of `override` it was never called through the base type, and every
  alias silently ran with the wrong arguments. Caught by a test.
- **`Execute` answers "did a command run", not "did it succeed".** A command that reports a
  problem still ran; the message carries the outcome. The dispatcher's catch-all turns a throwing
  command into a status message rather than the end of the session.
- **Console bindings are named actions, not evaluated code.** Ranger's cc.conf drives the console
  with `eval fm.ui.console.close()`. Canger's `config/cc.conf` now says `console_close`, which
  says what it does rather than how ranger implemented it.
- **Setting `ShowHidden` or `HiddenPattern` re-filters immediately**, as `SortOrder` already did.
  They were plain properties, so `:set show_hidden true` changed the setting and nothing visibly
  happened — found by driving the pty, not by a test.
- **Pressing up recalls the newest history entry**, not the one before it. The obvious
  implementation walks back from the newest and skips it.

### Known gaps

- `eval` is the only unhandled cc.conf directive. It executes arbitrary code and belongs with the
  plugin host in Phase 10; until then it is reported clearly rather than ignored.
- `RunProgram` reports "not implemented" — `:shell`, `:edit` and rifle all need the process
  runner, which is Phase 6. Nothing pretends to work.
- `paste` is not implemented: it needs the copy engine (Phase 7). `copy` and `cut` record the
  selection and the status bar reflects it.
- `tag_toggle` marks rather than tags, and `yank` reports rather than reaching the clipboard;
  both need state or a process that later phases add.
- `setlocal` at run time reports that it is configuration-only.
- Placeholders still present: `src/Canger.{Rifle,Preview,Vcs,Plugins}/AssemblyInfo.cs` and
  `tests/Canger.Rifle.Tests/PlaceholderTests.cs`.

---

## Phase 6 — Rifle + process runner ✅

Files open with the right program, and external commands run without wrecking the terminal.
619 tests passing, 53 commands.

Done:

- `Canger.Tui/Terminal` — `Suspend` and `Resume`, so a program can be handed the terminal in its
  normal state and Canger can take it back.
- `Canger.Tui/TerminalProcessRunner` — runs programs, choosing whether to suspend based on the
  flags. `TerminalEmulators` carries the table of ~25 emulators and how each wants to be told
  what to run.
- `Canger.Core/Processes/` — `ProcessFlags` with ranger's cancellation rule, `IProcessRunner`,
  and `IFileOpener`, which inverts the dependency so commands can open files without the domain
  knowing anything about rifle.
- `Canger.Rifle/` — condition parsing and evaluation (16 conditions), rule matching, numbering,
  and command construction. `RifleLauncher` implements `IFileOpener`.
- `Canger.RifleCli` — the standalone `rifle` program, with `-l`, `-p`, `-f` and `-c`.
- New commands: `open`, `open_with`, `draw_possible_programs`, `terminal`; `shell` and `edit`
  now actually run.

Verified against the real system: `rifle -l` picks sensible programs for `.md`, `.cs` and a
directory; `:shell touch x` creates the file and the listing refreshes; pressing `l` on a file
runs its rule; and a file named `; rm -rf ~` is passed as one argument with nothing executed.

### Decisions worth remembering

- **Only suspend for a program that will use the terminal.** A silent background job needs
  nothing, and suspending for it makes the screen flicker for no reason.
- **Files go in the shell's positional parameters**, as `set -- 'a' 'b'; command`, so a rule
  writes `"$@"` and quoting is handled once rather than in every rule. Names containing a NUL
  byte are dropped, because they cannot survive the shell at all.
- **`ext` is false for a directory, so `!ext` is true for one.** Real configurations rely on this
  to write directory-only rules; the shipped `rifle.conf` reaches its `ask` rule this way.
- **An unknown condition never matches**, so a typo disables its rule rather than enabling it for
  everything. A malformed regular expression does the same.
- **Numbering counts alternatives, not lines.** An uninstalled program leaves no gap, so
  `:open_with 1` means the same thing whatever is installed. A rule can still claim a fixed
  number with a `number` condition.
- **`open` decides on the selection, not the cursor.** With files marked, the user has asked for
  those files even if the cursor rests on a directory. The first version entered the directory
  and ignored the marks.
- **`console` *does* expand macros.** Marking it otherwise left `%space` literal on the command
  line, so `r` opened the prompt showing `open_with%space`. Found by driving the pty.
- **A user's `rifle.conf` replaces the shipped one** rather than adding to it, unlike `cc.conf`,
  because a rule's meaning depends on which rules precede it.

### Known gaps

- `eval` remains the only unhandled cc.conf directive; it needs the plugin host (Phase 10).
- `paste` still needs the copy engine (Phase 7).
- Captured output from `-p` (pipe to a pager) reports only its first line, because there is no
  pager yet. That arrives with previews in Phase 8.
- `yank` reports rather than reaching the clipboard; it now could run a program, but wiring it to
  the right one per platform is left with the rest of the clipboard work.
- `tag_toggle` still marks rather than tags; tags need their own persistent store.
- Placeholders remaining: `src/Canger.{Preview,Vcs,Plugins}/AssemblyInfo.cs`.

---

## Phase 7 — Copy engine ✅

Copying and moving, with **both features the brief asked for beyond ranger**: reflink copies and
a remaining-time estimate. 668 tests passing, 57 commands.

Done:

- `Canger.Core/FileOperations/` — `CopyEngine` (reflink, then `copy_file_range`, then buffered),
  `MetadataCopier`, `SafePath`, `TransferRate`, `CopyProgress`, `CopyJob`.
- New commands: `paste`, `paste_ext`, `delete`, `trash`.
- `IFileSystem.OpenWrite`, so the fallback copy goes through the abstraction and is testable.
- The status bar shows what background work is doing, and the listing re-reads itself when work
  finishes, reporting how it went.

### The two extra features, measured

Copying 200 MB on btrfs, verified with `btrfs filesystem du`:

| | time | space used | shared |
|---|---|---|---|
| plain copy | 108 ms | 400 MiB | 0 |
| Canger (reflink) | **8 ms** | 400 MiB total | **200 MiB shared, 0 exclusive** |

And the same three files across filesystems, where reflink cannot apply: `472 M actually moved`,
throughput and ETA both reported. In the browser, pasting two files reports
`done: 2 files, reflinked 2 (instant)`.

### Decisions worth remembering

- **Reflinked bytes are excluded from the throughput average.** A reflinked file transfers
  nothing, so feeding its size into the rate claims an impossible speed and collapses every later
  estimate to zero — the copy would appear to be finishing instantly right up until it did not.
  `CopyProgress` keeps two counts: `CompletedBytes` drives the percentage and counts everything,
  `TransferredBytes` drives the rate and counts only what moved.
- **The percentage still counts them.** A reflinked file is finished, so a directory of reflinks
  must not appear stalled at zero.
- **The rate is a decaying average**, not a total over elapsed time, because throughput changes
  when a copy crosses onto a different disk and a rate that never forgets would quote the old
  speed minutes later.
- **A relative destination path crashed the engine.** `Path.GetDirectoryName("copy.bin")` is the
  empty string, and the same-filesystem check asked for its status. Every test used absolute
  paths, so only running it by hand found it. There is a regression test now.
- **`CopyEngine` needs real file descriptors** for the reflink and kernel-copy paths, so it
  cannot work against a test double at all. It now detects that and copies through
  `IFileSystem` streams instead — which is also the honest fallback for any future virtual
  filesystem.
- **Reflink tests must not run in the system temporary directory**, which is very often a tmpfs
  and cannot reflink. They build their tree under `artifacts/` beside the repository instead, so
  the fast path is genuinely exercised rather than skipped everywhere.
- **A clash renames rather than overwrites.** Silently destroying what is already there is the
  worst possible default. `paste_ext` renames as `report_.pdf` rather than `report.pdf_`, so the
  copy still opens in the right program.
- **A failure on one file is recorded and the rest continue.** Abandoning a directory copy
  because one file could not be read is worse than finishing and saying what was missed.

### Known gaps

- `eval` remains the only unhandled cc.conf directive; it needs the plugin host (Phase 10).
- No task view yet, so a queued transfer is visible in the status bar but cannot be paused,
  reordered or cancelled from the interface. `TaskQueue` supports all three; only the widget is
  missing (Phase 9).
- `trash` delegates to `trash-put` and reports plainly if it is not installed, rather than
  implementing the desktop trash layout.
- `delete` does not yet honour `confirm_on_delete`; the console's question mode exists and is
  tested, but is not wired to it.
- Placeholders remaining: `src/Canger.{Preview,Vcs,Plugins}/AssemblyInfo.cs`.

---

## Phase 8 — Previews ✅

The third column shows what a file contains, and long output has somewhere to go.
735 tests passing, 60 commands.

Done:

- `Canger.Tui/Rendering/AnsiParser` — reads the colour codes preview scripts emit, discards
  anything that would move the cursor, and reduces 24-bit colour to the nearest palette entry.
- `Canger.Core/Previews/` — the exit-code protocol, `PreviewCache` keyed by size with wildcards,
  and `IPreviewProvider`.
- `Canger.Preview/` — `ScopeScriptRunner`, `ScriptPreviewProvider`, and the image displays
  (`kitty`, `ueberzug`, and a no-op).
- `Canger.Ui/Widgets/Pager` — scrolling text with colour preserved, used both for previews and
  full screen.
- New commands: `reset_previews`, `display_file`, `pager_close`. `:shell -p` now pipes into the
  pager instead of reporting one truncated line.

Verified through a pty: text files preview with syntax highlighting from the shipped `scope.sh`,
directories preview as listings, `i` opens the pager full screen and PageDown scrolls it, and
`:shell -p ls -la` shows its whole output.

### Decisions worth remembering

- **The exit code is the whole protocol**, and it is ranger's unchanged, so an existing
  `scope.sh` works as-is. 0 success, 1 nothing, 2 read it as text ourselves, 3/4/5 fix width /
  height / both, 6 image at the cache path, 7 the file itself is an image.
- **The cache is keyed by size with wildcards.** A preview that says it does not depend on a
  dimension is stored under `-1` for it and matches any value, which is what makes a resize cheap:
  syntax-highlighted source is regenerated only when the width changes, and a metadata listing
  never.
- **Images cannot go through the screen buffer.** The terminal draws them itself, over cells the
  buffer leaves blank, so they are written *after* the flush — drawing first would let the flush
  paint over them. They are also only redrawn when they change, since re-sending an image every
  keystroke makes the whole display flicker.
- **Directories are previewed directly, not by the script.** The browser already knows how to
  read a directory; shelling out would be slower and inconsistent with the columns beside it.
- **A hanging script is abandoned.** A preview is a convenience, and a script stuck on a network
  filesystem or a corrupt archive must not take the browser with it. Ten seconds by default.
- **The script runs beside the file, but only if that directory exists.** Setting a working
  directory that does not exist makes `Process.Start` fail outright, losing the preview for a
  reason unrelated to the file. Caught by a test using a synthetic path.
- **`Executables` moved from `Canger.Rifle` to `Canger.Core.Processes`** — finding a program on
  the path is not a rifle concern, and three places wanted it. `TerminalEmulators` had grown its
  own copy, which is now gone.
- **Binary content previews as nothing.** Filling the column with rubbish is worse than an empty
  column, and can confuse the terminal.

### Known gaps

- `eval` remains the only unhandled cc.conf directive; it needs the plugin host (Phase 10).
- Five of ranger's eight image protocols are not implemented: w3m, iterm2, sixel, terminology and
  urxvt. Naming one shows previews without images rather than failing, which is the same outcome
  as an unsupported terminal. kitty and ueberzug cover the common cases and the sample
  configuration.
- The image displays are untested by the suite: they need a terminal that can show images.
  `ImageDisplayFactory` falling back is covered by the protocols reporting themselves unavailable.
- No task view widget yet, so a transfer cannot be paused or reordered from the interface
  (Phase 9).
- `delete` does not honour `confirm_on_delete`; the console's question mode exists and is tested
  but is not wired to it.
- Placeholders remaining: `src/Canger.{Vcs,Plugins}/AssemblyInfo.cs`.

---

## Phase 9 — Remaining state and views ✅

885 tests. Everything ranger shows on screen is now implemented; only `eval` and the Phase 10
subsystems remain.

**Bookmarks** (`State/Bookmarks.cs`) — char-keyed, persisted to
`~/.local/share/canger/bookmarks`, with the three-way merge on save so two running instances do
not lose each other's entries. `'` and backtick are aliases for the previous directory, fed by
`Tab.Left`. This also made `%any_path` resolve.

**Tags** (`State/Tags.cs`) — persistent per-file marks in `~/.local/share/canger/tagged`, in
ranger's format (a bare path means the default `*` tag, otherwise `t:/path`). Reloaded before
every change, for the same reason bookmarks are merged.

**Task view** (`Widgets/TaskView.cs`) — cursor, reordering, cancel and pause. Ranger drives this
through Python `eval`; the bindings were rewritten as named commands (`task_move_down`,
`task_move_up`, `task_remove`, `task_pause`), which is what a C# port should do with `eval
fm.ui.taskview.task_move(-1)`.

**Linemodes** (`Model/Linemode.cs`) — the eight ranger defines: `filename`, `metatitle`,
`permissions`, `fileinfo`, `mtime`, `sizemtime`, `humanreadablemtime`,
`sizehumanreadablemtime`. Two details worth keeping in mind:

- `Detail` returning `null` means "the column decides", which is how ranger's
  `raise NotImplementedError` reads in C#. An empty string means "show nothing", which is a
  different thing — `permissions` uses it because its data is in the title instead.
- A mode that needs metadata an entry lacks falls back to `filename` **for that entry alone**,
  so a half-annotated library still lists cleanly.

`LinemodeSelector` resolves per entry: an explicit `:linemode` override first, then the
`default_linemode` rules newest-first (`always` / `path=` / `tag=`), then the default. Plugins
register their own through `LinemodeRegistry`, and re-registering a built-in name replaces it,
which is how a decoration plugin prefixes icons.

**Metadata** (`State/FileMetadata.cs`) — the `.metadata.json` database `metatitle` reads, with
`:meta` and `:prompt_metadata`. Keys are free-form; `metadata_deep_search` walks up the tree.

**`fileinfo` runs `file(1)` off the drawing path.** Ranger launches one process per row while
drawing, which stalls a large directory. `FileDescriber` caches by path and modification time and
answers from the thread pool; a row shows nothing until its answer lands, then a redraw fills it
in. This is the one place Canger deliberately does not reproduce ranger's timing.

**Colourschemes** — `jungle`, `snow` and `solarized` alongside the default. Jungle *derives* from
`DefaultColorScheme`, as in ranger, so it inherits whatever the default gains. `ColorSchemeRegistry`
holds factories rather than instances, because each scheme caches its own results.
`SwitchableColorScheme` wraps the current one so `:set colorscheme` takes effect at once — the
widgets hold one reference for their lifetime and the wrapper swaps what is behind it.

**Deletion confirmation** — `confirm_on_delete` and `confirm_on_trash` now reach the console's
question mode through `IFileManager.Ask`. `multiple` (the default) asks only for more than one
entry or a non-empty directory. An abandoned question never calls back, which is what makes
Escape safe. `like_delete` defers to `confirm_on_delete`.

**Borders and line numbers** — both settings now do something. `draw_borders` handles
`none`/`outline`/`separators`/`both` (and `true`, for older configurations), with tees where
rules meet the frame. Line numbers handle `absolute` and `relative`, honouring `one_indexed` and
`relative_current_zero`; the number is right-aligned to the widest in view and dropped entirely
rather than crushing the name in a narrow column, which is ranger's rule.

**Multipane view** (`Views/MultipaneView.cs`) — every tab side by side, equal widths, every pane
a main column with its own cursor. `draw_borders_multipane` adds `active-pane`, which frames only
the focused pane; unset (not `"none"`) means "whatever `draw_borders` says".

**Tabs.** The tab commands did not exist at all — `gt`, `gn`, `<C-w>`, `<a-N>` all parsed and
then resolved to nothing, which is why multipane initially rendered one pane. `tab_new`,
`tab_open`, `tab_close`, `tab_move`, `tab_shift` and `tab_restore` are now implemented, with
ranger's semantics: `tab_move` wraps and steps over gaps rather than counting numbers,
`tab_shift` takes an *offset* and pushes displaced tabs aside rather than overwriting them, and
the last tab is never closed. Closed tabs are kept for `tab_restore` (`uq`).

Verified through the pty harness as well as the suite: all four colourschemes switching live,
every linemode, both answers to the delete confirmation, all border modes in both views, and two
tabs side by side.

---

## Phase 10 — eval, plugins, VCS, CLI, man page ✅

979 tests. Every ranger feature is now ported; the shipped configuration parses with no unhandled
directives for the first time.

**`eval` is gone from the shipped configuration, and implemented for the ones that remain.**
The thirteen `map ... eval ...` bindings became named commands, which is what a C# port should do
with a line like `eval fm.cut(dirarg=dict(down=1), narg=quantifier)`:

- `cut` and `copy` now take a direction and a `mode=` (`set`/`add`/`remove`/`toggle`), so `dj`,
  `dgg`, `yk` and friends are ordinary command lines. A bare quantifier means "this many
  downwards", so `3yy` still works.
- `rename_append` takes `where=extension|end|start`, covering `a`, `A` and `I` with one command
  instead of ranger's hard-coded cursor offset of 7.
- `cd` expands `$VAR` and `${VAR}`, which covers `gi`; `gR` works through the same mechanism
  because Canger publishes `CANGER_INSTALL_DIR` and `CANGER_CONFIG_DIR` to itself and its children.
- `console_history` opens the prompt at an earlier command, replacing a `chain` of two actions
  whose second reached into the console widget.
- The ten `eval for arg in "rwxXst"` loops became sixty explicit `map` lines. The loop was a
  Python convenience, not a feature, and the explicit form is greppable.

`eval` itself still exists, as both a directive and a command, and evaluates **C#** rather than
Python — `eval foreach (string a in new[] {"r","w"}) cmd($"map +u{a} chmod u+{a}");`. Snippets are
compiled individually so they run exactly where they appear (the keybinding trie is
order-sensitive) and cached on disk by content, so the cost is paid once.

**Plugin host** (`Canger.Plugins`). `commands.cs`, `plugins/*.cs` (compiled together, so a plugin
can span files) and `plugins/*.dll`. Names beginning with `_` are skipped without being compiled —
which matters, because a disabled plugin is usually disabled for being broken. A plugin that fails
to compile is reported with full diagnostics under `--config` and skipped; Canger is how the user
would go and fix the file. `ICangerPlugin` has `OnInit` (before the interface) and `OnReady`
(after the first paint); `[Command]` classes and `ILinemode` implementations are found by
reflection and need no interface.

Two things worth knowing:

- `CSharpCompilationOptions.usings` applies **only to script compilations** and is silently
  ignored by a regular one. The implicit namespaces have to go in as a synthetic file of
  `global using` directives, or every plugin fails to compile with "CangerCommand not found".
- `PluginLoadContext.Load` returns `null` on purpose, deferring to the default context. A plugin's
  `CangerCommand` must be the *same type* as Canger's or the registry will not recognise it, so
  sharing is the point rather than a compromise.

**VCS** (`Canger.Vcs`) — git, Mercurial, Subversion, Bazaar. `IVcsBackend` is a stateless strategy
rather than ranger's runtime class-swapping; the state ranger keeps on the mutated object lives in
`VcsRepository`. A single background worker answers status requests, because several git processes
over one repository contend for the same index lock and end up slower than one. The listing shows
whatever was last known and redraws when a fresh answer lands.

- Everything goes through machine-readable formats (`--porcelain -z`, `--template json`,
  `--xml`), so a filename containing a space or a newline parses correctly.
- Git reports a rename as *two* NUL-separated records and only the first carries a status;
  consuming the second invents a status for a path that does not exist.
- Commit messages have control characters replaced. A commit summary is drawn on the status
  line, and an escape sequence in one must not be able to repaint the screen.
- `Canger.Vcs` deliberately references nothing, which is what lets `Canger.Core` depend on it.
  The scaffold had it referencing Core, which was a cycle waiting to be discovered.

**CLI.** A real parser replaced the three-flag `flags.Contains` check: `--clean`, `--copy-config`,
`--choosefile`, `--choosefiles`, `--choosedir`, `--selectfile`, `--cmd`, `--confdir`, `--datadir`,
`--cachedir`, `--show-only-dirs`, `--list-tagged-files`, `--help`, `--version`, `--man`. An
unrecognised option is now reported rather than ignored. The choosers work by substituting the
`IFileOpener`, which covers every route to opening a file — Enter, the right arrow, `:open_with`,
a mouse click — without any of them knowing the mode exists.

**Man page** — `--man` writes it, and `doc/canger.1` is the generated copy. The reference sections
(options, bindings, settings, commands) come from the same metadata the program uses, so they
cannot drift; only the prose is hand-written. A binding for `.` would otherwise be read as a macro
and swallow the rest of the page, so text is escaped.

### A data-loss bug found on the way

Cutting a directory and pasting it inside itself **deleted it and everything under it**. The move
renamed it beneath a path that was about to stop existing, then removed the source. Pasting a file
into the directory it already sat in lost it the same way. `CopyJob` now refuses both before
touching anything — ranger guards this with `shutil`'s `_destinsrc`, which the port had missed
entirely. Six regression tests cover it, including that a *copy* into one's own directory must
still work (the clash policy renames it) and that `/srcx` is not treated as inside `/src`.

---

## Fixes from first real use

Found by running Canger against a real configuration copied from ranger. 998 tests.

**`]` and `[` did the wrong thing entirely.** `move_parent` was implemented as "go up N
directories". Ranger's is quite different: it moves the cursor in the *parent* listing and enters
what it lands on, so the keys step between sibling directories without leaving the depth you are
at. Canger's version additionally steps *over* files rather than stopping on them — ranger indexes
straight into the parent's listing and silently does nothing when it lands on a file, which makes
the key a dead end wherever siblings are mixed.

**Directory entry counts only appeared for visited folders.** `automatically_count_files` was
parsed and ignored. The comment claiming a count would "make entering a large tree crawl" was
simply wrong: it is one shallow read per *visible* row, cached, which is what ranger does. Now
implemented, gated on the setting, with the count cached per node.

**`default_linemode` in a configuration file did nothing.** It was implemented as a command but
never registered as a directive, and the reader rejected anything that was not a directive. Fixed
generally rather than specifically: the reader now has a `Fallback`, and any line naming a command
is queued and run once the browser exists. That is what cc.conf lines are — commands — and it
means a command a plugin defines is usable in a configuration file too.

**`nested_ranger_warning` was rejected.** The setting had been renamed `nested_canger_warning`
along with everything else carrying the program's name, but a configuration carried over from
ranger still uses the old spelling, and rejecting a whole file over one word is a poor greeting.
Ranger's name is now accepted as an alias, resolving to the canonical one so the value lands where
it is read. A name diff against `ALLOWED_SETTINGS` confirms this was the only one.

**Python `eval` lines produced thirteen C# compiler errors each.** Canger's `eval` is C#, which is
right, but ranger's own configuration contains ten Python `eval` loops and the failure was a wall
of `CS1003`s referring to a temporary file the user has never seen. Now one line per snippet,
saying the language changed. Nothing is lost in practice: those loops generate the chmod bindings,
which Canger's shipped cc.conf writes out explicitly, and cc.conf is additive.

**Forked programs wrote onto the alt screen.** A rule with `flag f` — every video player rule —
inherited Canger's terminal, so mpv announcing its codecs scrolled the display and corrupted it,
and the screen jumped when a video opened and again when it closed. Ranger forks with `setsid`
and sends stdin, stdout and stderr to `/dev/null`; Canger did neither. Now it does both.

**A configuration carried over from ranger trapped the user in the console.** Ranger's own
`rc.conf` binds most of the console — `<ESC>`, `<CR>`, the arrows, every editing key — to
`eval fm.ui.console.…`. Because cc.conf is additive, those lines *override* Canger's working
bindings with Python that cannot run, and since Escape and Enter are among them the prompt could
not be left: the user had to kill the terminal. `RangerEvalTranslator` now maps ranger's own eval
forms onto Canger's named actions at config-read time. It is a fixed table, not an interpreter,
covering exactly what ranger ships; anything else still reaches the eval machinery untouched.

### Second round, from continued use

**The interface never came back after opening a file.** `TerminalProcessRunner` raised a
`Resumed` event and **nothing subscribed to it**. Every file opened through rifle runs its program
without returning through `Browser.RunProgram`, so nothing knew the terminal had been taken and
given back — and the screen buffer diffs against what it last wrote, which after another program
has drawn over the terminal is fiction. The alt screen came back and stayed blank. `Resumed` is
now part of `IProcessRunner`, the browser subscribes, and the next frame is written in full.
This is the single worst defect found so far and it was invisible to the unit tests, which never
run a process.

**An MP3 opened HandBrake.** Canger asked only `file --mime-type -Lb`, which returns
`application/octet-stream` for some perfectly ordinary MP3s. No `mime ^audio` rule matched, so the
file fell through to `xdg-open`, and on a machine with video-editing software installed the
desktop's default for `audio/mpeg` is HandBrake. Ranger does something quite different: it guesses
from the **extension first**, falls back to `file(1)`, and if that says `octet-stream` tries
`mimetype(1)`. Canger now does the same three steps, reading the system's own `mime.types` tables
so it agrees with the rest of the machine. The comment that previously justified the single-step
version — that `file` is used "because the whole point of the mime condition is to work on files
whose names say nothing useful" — had the trade-off exactly backwards.

**The hint window.** Pressing `g` now lists what can follow, as ranger does, grouped by first key
and collapsed a group at a time past `hint_collapse_threshold`. It was simply never implemented.

**More ranger eval forms translated.** `fm.cd(ranger.RANGERDIR)`, `fm.tab_new('%d')` and
`fm.tab_new(narg='Name', path="…")` — the last being how a configuration keeps a set of working
directories one keystroke away. `tab_new` grew a `label=` argument to carry the name. Note that
the value has to be read from the *original* text rather than the whitespace-normalised copy used
for matching: real paths contain spaces, and `~/Documents/SENSITIVE DATA` is not
`~/Documents/SENSITIVEDATA`.

**`devicons` is now built in.** A configuration carried over from ranger says
`default_linemode devicons`, which in ranger comes from a *Python* plugin — so Canger reported
"Invalid linemode: devicons" and showed no icons. The tables are data and the resolution is three
lookups (whole filename, then extension, with a separate table for directories), so there is
nothing about it that needs to be a plugin. `tools/generate-devicons.py` reads
`ranger_devicons/devicons.py` and emits `DeviconTables.g.cs` — 88 exact names, 227 extensions, 15
directory names — because a transcription slip among several hundred glyphs would show as one
wrong icon nobody would ever track down. Run the generator again to pick up a newer plugin
version. The glyphs need a patched font (Nerd Fonts), which is why this is not the default.

**A ranger `commands.py` cannot load, and that is by design — but the migration is now cheap.**
Custom commands are the one part of a ranger configuration that genuinely has to be rewritten:
Canger compiles C#, and no amount of shimming turns a Python class into one. What was missing was
any help doing it. `tools/port-ranger-commands.py` reads a `commands.py` and emits a `commands.cs`,
porting every command whose whole body is one shell call — which is most of them. Run against the
sample configuration it ports **35 of 51**, including the ones taking an optional argument with a
default (`image_convert` and its 1080). The remaining 16 are listed in a comment at the top of the
generated file rather than guessed at, because they drive the file manager itself: fzf and fd
integration, `paste_ext`, `open_in_tabs`, the similar-file selectors.

Worth knowing: a *thin* wrapper needs no C# at all. `alias image_watermark shell
image-watermark-tui %s` in cc.conf does the same job, and aliases carry arguments and macros. The
generator exists for the ones that need a default value in the middle of a command line, which an
alias cannot express.

**The shipped `commands.cs` was never loaded.** `cc.conf` is read from the install directory,
then `/etc`, then the user's — additively. `commands.cs` was read from the user's directory *only*,
so the file Canger ships was nothing but a thing to copy. Ranger loads its own `commands.py` and
then a personal one; Canger now does the same, which also means a user's file only has to contain
what it adds.

**The shipped `commands.cs` did not compile.** It was written in Phase 0 against an API that
never materialised — a `Tab(int)` override with the wrong signature, and `FileManager.EditFile`,
which does not exist. So anyone who copied it, which is the entire purpose of the file, got a
compile error and *no custom commands at all*. Nothing caught it because nothing ever compiled it:
the plugin host has thorough tests, and every one of them writes its own source. `commands.cs` is
now two working examples — one showing that a thin wrapper needs no C# at all, one showing the two
things an alias cannot do (a defaulted argument and completion) — and
`ShippedCommandsTests` compiles the real shipped file and asserts it registers something.

**An image preview survived into whatever got the terminal next.** Opening a shell with `S`
left the ueberzug overlay sitting on top of it. The image is drawn by a helper outside the screen
buffer, so clearing the buffer does not remove it — and nothing told the helper anything on the way
out. `IProcessRunner` now has a `Suspending` event paired with `Resumed`; the browser clears the
overlay on the way out and forgets what it had shown, so it is sent again on the way back.

**A program that failed said so only in a frame that was immediately painted over.** The shell
writes `command not found` to the terminal it was given, and the repaint on resume wipes it. Canger
now reports a non-zero exit on the status line, naming 127 and 126 specifically — those are almost
always a configuration problem rather than the program failing. It printed the whole PATH for 127
as well until the launcher below made that moot; a hundred characters of PATH only pushed the
program's name off the status line, so the message is now just "not found on PATH".

**`command not found` was a bare PATH, and the diagnostic above is what proved it.** Canger was
being started from a window-manager keybinding, so it inherited
`/opt/anaconda3/bin:/opt/anaconda3/condabin:/usr/local/bin:/usr/bin:/bin:...` — no
`~/.local/bin`, where every one of the personal scripts a command runs lives. Ranger works because
it is launched through a xonsh script that does `source ~/.xonshrc`, and that is where
`~/.local/bin` joins PATH.

`canger.sh` is the launcher, symlinked onto `~/.local/bin/canger`. It sets only what Canger itself
reads — PATH, VISUAL, EDITOR, TERMINFO — rather than sourcing a shell profile, which is what made
it 28ms instead of the 1.65s a Python interpreter costs. Two details that are easy to get wrong:
it resolves its own directory with `readlink -f` rather than treating `$0` as a path, because it is
reached through a symlink and the build lives beside the real file; and it prepends to PATH only
when the entry is missing, so repeated launches cannot pile it up.

An earlier `canger.xsh` did the same job by sourcing `~/.xonshrc`. It has been deleted: the shell
script covers it at a sixtieth of the cost, and keeping two launchers meant two places to update.
Should the full xonsh environment ever be wanted, `exec canger` from a xonsh script does it.

**Devicons took two seconds to appear.** Anything applied when the interface becomes ready — a
deferred `default_linemode`, a plugin's `OnReady` — landed *after* the first frame was drawn, and
the loop then blocked for the whole of `idle_delay` before drawing again. Drawing once more
immediately after the ready handlers took the devicon-specific lag from ~2s to 0.01s; icons now
appear with the first listing. What remains is 1.5s of .NET startup, which is a different problem.

**`abort` did not exist.** Both ranger's configuration and Canger's bind `map <C-c> abort`, and
Canger had no such command — so once Ctrl-C started arriving as a keystroke it resolved to nothing.
It now does what ranger's does: cancel the running background task, or, with nothing running, say
how to quit. Which is worth answering, because Ctrl-C is what someone presses when they want out.

Worth recording that this binding has *never* worked in ranger either, for the same reason it did
not work here: Ctrl-C reaches ranger as a signal and kills it first, which is why pressing it there
closes the terminal window. Canger delivering it as a keystroke is the first time the line in the
configuration means anything.

**Ctrl-C destroyed the terminal.** Raw mode deliberately left signal generation alone, with a
comment about Ctrl-C reaching the shell's job control — which was the wrong call for a full-screen
program. SIGINT killed the process outright with the alternate screen still in use and the driver
still in raw mode, leaving a shell prompt in the middle of a half-drawn interface. It also meant
`map <C-c> abort`, which both ranger and Canger ship, could *never* fire: the terminal never
delivered the key.

Ranger gets away with `cbreak()` because Python turns SIGINT into a catchable `KeyboardInterrupt`;
.NET simply terminates. The fix is to clear the interrupt and quit *characters* rather than the
`ISIG` flag, so Ctrl-C and Ctrl-Backslash arrive as ordinary bytes while Ctrl-Z still suspends.

A signal from anywhere else — `kill`, a closing terminal, a logout — still ends the process, so
SIGINT, SIGTERM, SIGHUP and SIGQUIT now restore the terminal on the way out without cancelling the
signal. Verified: Ctrl-C is absorbed and Canger keeps running; SIGTERM and SIGHUP both leave the
alternate screen behind them.

### Previews were generated on the drawing path

Moving the cursor onto a video ran `scope.sh` synchronously and waited for it. Measured on the
sample tree that is **381ms** for an mkv — a whole ffmpeg run for the thumbnail — and 48ms for a
Matroska audio file. The browser stopped dead for that long on every keypress, which is what
"sluggish, not snappy like ranger" was. Ranger runs previews through its loader, off the drawing
path, which is why it does not feel like this.

Previews now generate on a worker, keyed so a redraw asking about the same row repeatedly does not
start a second ffmpeg. Cursor movement in the video folder went from up to 381ms to **3ms average,
10ms worst**, and the thumbnails still arrive — they simply arrive after the frame rather than
holding it up. The provider keeps its synchronous path for `--list` and for the tests, where there
is no loop to hold up and a callback would just complicate them.

**A flag cannot end a wait that has already begun.** With generation moved to a worker the answer
arrived while the main loop sat in `poll()` for the whole of `idle_delay`, so a preview that took
fifteen milliseconds took a second and a half to appear. `Terminal.Wake()` writes a byte down a
pipe the poll also watches. The file describer and the version-control worker had the same latency
and now go through the same path.

**Empty thumbnails are treated as no thumbnail.** `ffmpegthumbnailer` creates its output file
before it discovers a Matroska file has no video stream, so every `.mka` left a nought-byte file in
the cache — 18 of the 24 files there. The litter is the tool's, and ranger accumulates it too, but
handing a nought-byte file to an image protocol achieves nothing except a corrupted screen.

### Startup: 3.2 seconds to 0.3

Chasing "why do the devicons take five seconds" turned up three separate costs, two of them
self-inflicted:

- **The xonsh launcher cost 1.65s.** It exists to `source ~/.xonshrc` for the PATH, and that means
  starting a Python interpreter every time Canger opens. `canger.sh` sets the four variables Canger
  actually reads — PATH, VISUAL, EDITOR, TERMINFO — in POSIX sh: **28ms** against the binary's own
  26ms. The xonsh launcher has since been deleted rather than kept as an alternative.
- **Both `commands.cs` files shared one cache slot.** Loading the shipped file as well as the
  user's — added earlier the same day — gave two different compilations the same cache name, so
  each deleted the other's build as stale and *both* recompiled from source on every launch. The
  name now includes a hash of the directory it came from.
- **Ten Python `eval` lines were handed to Roslyn to watch them fail, every launch.** The
  "this is C# now, not Python" check ran on the diagnostics *after* compiling. Moving it in front
  took `--config` from 961ms to 171ms.

Measured end to end, with the real configuration: the listing is on screen at **287ms** and the
devicons at **294ms**. It was over three seconds.

### Tags were stored but never drawn

Pressing `t` did nothing visible. Everything about tagging worked except the part the user can
see: the keys were bound, `tag_toggle` ran, the tag reached `~/.local/share/canger/tagged` in
ranger's own format — and `BrowserColumn` never mentioned tags at all. The colourschemes even had
styling for `tag_marker` and `tagged` waiting for a caller that did not exist. Nothing failed, so
nothing caught it; the render tests asserted the *names* on each row and a missing marker changed
none of them.

The marker is one cell immediately left of the name, showing the tag character — `*` by default,
or whatever `map "<any> tag_toggle tag=%any` supplied. The cell is claimed whether or not the file
is tagged (`browsercolumn.py:479-487`), which is what stops a listing sliding sideways the moment
one row gains a marker. Order across the left of a row is number, gap, tag, name.

Verified by running ranger 1.9.4 from `ranger-master/` under a pty beside Canger, with the same
tree and the user's own configuration, and comparing the raw escape sequences:

    ranger:  \x1b[3;15H  1  ' '  \x1b[91m*  \x1b[94m<icon> subdir
    canger:  \x1b[3;15H  1  ' '  \x1b[91m*  \x1b[94m<icon> subdir

Same row, same column, same bright red. The two tag files came out identical too.

**Two further defects fell out of the same investigation.**

Tags key on the *resolved* path (`core/actions.py:880`), which Canger was not doing: it recorded
`FsNode.Path`. Tagging a symbolic link therefore wrote the link's own path, and ranger — reading
the same file — would look for the target and find nothing. `FsNode.RealPath` now resolves the
link chain once and caches it, so tagging `alias.txt` marks `real.txt` and *both* rows show the
marker, which is the whole point of keying it that way. `mark_tag`, `unmark_tag` and the
`default_linemode in tag=x` scope were all keyed the same wrong way.

The marked-entry prefix was an asterisk. That is the *tag* marker's glyph, so the one character a
user looks for to mean "tagged" already meant "marked" — and ranger draws no glyph there at all.
It shifts a marked name one cell right and recolours it (`browsercolumn.py:367-369`), verified in
the same side-by-side capture: `\x1b[0;1m\x1b[33m1   <icon> other.txt`, three spaces and no
asterisk. Canger now does the same. Marks stay obvious because `marked` is already yellow and
bold in every colourscheme.

Not divergences, checked while in here: `Tags.Toggle` matches ranger's `tag in (existing,
default)` rule exactly, so `t` on a file carrying a custom tag clears it; and re-reading the tag
file before every change is deliberate in both, so two instances cannot clobber one another.

### `:yank` never touched the clipboard, and sixteen bindings named nothing

`yp` put the path on the status line and stopped. The command had a comment saying the clipboard
would follow "when the process runner lands"; the runner landed in phase 6 and this was never
finished, so every one of `yp` / `yn` / `yd` / `y.` reported success and copied nothing.

There is no system call for a clipboard — X11 and Wayland both keep the selection in a client, so
the only way to set it is to hand the text to a program that will hold it. `SystemClipboard`
reproduces ranger's table and its order of preference exactly (`config/commands.py:2060-2083`):
pbcopy, xclip, xsel, wl-copy. The detail that matters most is that **X11 has two independent
selections** and each helper sets only one, which is why xclip and xsel are run twice — once for
the primary selection that middle-click pastes, once for the clipboard that Ctrl-V pastes. Copying
to only one of them is the commonest way for this to look broken. The text goes in on standard
input, never as an argument, because a filename may contain anything at all.

Three smaller things were wrong in the same command: it joined with spaces where ranger joins with
newlines, so a marked selection came back unusable; `dir` reported the current directory rather
than each entry's own, which differs under `:flat`; and the default mode used the display path
rather than the basename, which under `:flat` carries directories nobody asked to copy. It also
had no tab completion for its four mode names.

**The audit that came out of it matters more than the fix.** A binding is stored as the text of a
command line and only resolved when the key is pressed, so one naming a command that does not
exist sits there silently until someone presses it and gets nothing — which is exactly how `yank`
hid, and how sixteen others were hiding. `--config` now resolves every browser binding against the
registry and names the ones that do not resolve. On the real configuration it found **72**.

Sixteen were ranger built-ins with no Canger command at all, all now ported:

| command | key | note |
|---|---|---|
| `search_next` | `n`, `N`, `cc`/`ci`/`cm`/`cs`/`ct` | six orders; the chosen one sticks, as ranger's does |
| `chmod` | `=` | octal, or a quantifier: `644=` |
| `bulkrename` | `cb` | edit a list of names in `$VISUAL`, then apply the differences |
| `get_cumulative_size` | `dc` | parallel walk; follows file links, never descends a linked directory |
| `traverse` / `traverse_backwards` | `<CR>` | walks the whole tree one entry at a time |
| `toggle_visual_mode` / `change_mode` | `V`, `uV`, `<Esc>` | including `reverse=True`, which unmarks as it moves |
| `paste_symlink` / `paste_hardlink` / `paste_hardlinked_subtree` | `pl`, `pL`, `phl` | |
| `display_log` | `W` | needed a message log, which nothing kept |
| `scroll_preview` | `<C-e>`/`<C-y>` | needed the preview pager to stop being reset every frame |
| `jump_non` | `)` | with `-r` and `-w` |
| `help` | `?`, `<F1>` | the four-way question, then man page / bindings / commands / settings |
| `exit` | | |

Two of those needed something underneath first. `display_log` had nothing to show because messages
were overwritten in place and never kept — a notification that happens twice in quick succession
was gone before it could be read. And `scroll_preview` did nothing visible because `RenderPreview`
called `Pager.SetText` on **every frame**, and setting the text returns the pager to the top; it
now only sets it when the text has actually changed, which also stops re-splitting the same string
sixty times a second.

The remaining 47 unresolved bindings are all the user's own — 29 commands from their `commands.py`
that `tools/port-ranger-commands.py` did not translate, plus `zi` (the zoxide plugin) and
`extract_to_dirs` (ranger-archives). Those are not port gaps; they are listed under **What is
left**.

A last parity fix while in here: `yank` still reports what it copied and which helper took it.
Ranger says nothing at all, and a yank is otherwise completely invisible until something is pasted
somewhere else — and with no helper installed, silence is indistinguishable from success.

### The user's own 51 ranger commands, ported

All of them now resolve. What was there before was 35 generated commands, **six of which were
silently wrong**, and sixteen names that resolved to nothing.

**The six wrong ones were the generator's fault, and worth understanding.** It matched the command
string with a regex over the unparsed Python, which truncated

    self.fm.execute_console(f'''shell -f gpg --detach-sign -u KEY "{f.relative_path}" ''')

at the inner quote and emitted `shell -f gpg --detach-sign -u KEY ` — gpg with no file. The four
gpg commands, `media_length_tag` and `directories_number_highlight` all ran their tool on nothing
and said nothing about it. A generator that silently produces a broken command is worse than one
that refuses, so it now:

- reads the template from the f-string's own AST parts instead of a regex, so a quote inside it
  cannot end the match;
- recognises the *run once per selected file* shape — ranger loops over the selection because
  these tools take exactly one argument — and emits a `foreach` with the name properly quoted,
  stripping the literal quotes the Python template wrapped it in;
- refuses a holder that is not assigned from `self.arg` or `self.rest`, which is what
  `directories_number_highlight` needed: its `target_dir` came from the *highlighted file*, and
  treating it as a typed argument produced a command with an empty string where the path went.

It now generates 33 and names 18 for hand-porting, up from 35 and 16 — a generator that admits to
less is the point.

**Fifteen were hand-ported** into the same file: `fzf_select`, `fzf_locate`, `fd_search`,
`fd_next`, `fd_prev`, `mkdirmv`, `toggle_flat`, `copy_selected_to_highlight`,
`directories_number_highlight`, `open_in_tabs`, `gpg_decrypt_file`, `file_number`,
`file_select_similar`, `file_copy_similar`, `mount`. Three more — `mark_tag`, `unmark_tag`,
`paste_ext` — are deliberately absent: Canger has them built in, and a copy would only shadow
the real thing.

**Two ranger plugins came with them**, because `ead` and `fh` are bound in cc.conf and neither is
a commands.py command: `plugins/zoxide.cs` (`z`, the `zi` alias, and the hook that teaches zoxide
where you go) and `plugins/archives.cs` (`compress`, `extract`, `extract_raw`,
`extract_to_dirs`). ranger-archives expresses its twenty formats as a chain of twenty
`elif search(regex)` branches each rebuilding a command list; here it is a table of rules the two
builders walk, which is the same information with the repetition removed and, more usefully,
readable in one screen.

Four things had to be added underneath.

**`IProcessRunner.RunCapturingOutput`.** A chooser is neither of the two shapes the runner had: a
program given the terminal has its output on the screen, and a program whose output is captured is
not given the terminal. fzf, zoxide's picker and `mmtui` all need the interface to step aside while
*only* standard output is a pipe — they draw through stderr or `/dev/tty`. Ranger reaches the same
arrangement with `execute_command(cmd, stdout=PIPE)`. Reading before waiting, because a chooser
listing a large tree fills the pipe and would otherwise deadlock.

**`IFileManager.DirectoryEntered`.** Ranger's `cd` signal, which is one of only two things real
plugins bind to — zoxide's database learns nothing without it. Raised from the loop by comparing
the current path, rather than from each of the dozen places that can change directory: one place
that notices beats twelve that must all remember to announce.

**`IFileManager.SelectPath`**, which existed on the browser but not on the interface, so no command
could reach it. Every chooser ends by naming a path and expecting the cursor to be on it.

**`System.IO` and `Canger.Core.Processes` added to the plugin compilation's implicit usings.** A
plugin doing anything with files needed the first and anything with a program needed the second.

One trap worth recording: `[GeneratedRegex]` cannot be used in plugin code. Canger's Roslyn host
runs no source generators, so the attribute leaves the partial method with no body and the whole
file fails to compile. A `static readonly Regex` is the substitute.

`--config` reports 1 unresolved binding, and says why: `zi` is an alias created in a plugin's
`OnInit`, which runs after the report — the report now says so rather than listing a working
binding as broken. Also fixed there: it split the command name on spaces only, and a `map` line
may separate the command from a trailing comment with **tabs**, so `cmd\t\t#` was taken as the
name and thirty perfectly good bindings were reported as missing.

`UserCommandsTests` compiles the copies in `artifacts/` — these are plugin sources, so nothing in
the solution references them and no compiler would otherwise see them, which is exactly how the
shipped `commands.cs` came to ship broken. It also asserts the file does not shadow a built-in.

Verified live in a pty against the real configuration: `ff` flattens and unflattens, `:mkdirmv`
moves a selection into a new directory, `,` strips `-01-02r-03p.mkv` down to `Alpha Series` and
marks the set, `:extract_to_dirs` unpacks `bundle.tar.gz` into `bundle/`, `:compress test.zip`
produces the zip, and `:z` with no match stays silent as it should.

### A listing was three colours instead of ranger's eight

Everything was cyan, white or green: directories blue, executables green, links cyan, everything
else the terminal default. Ranger colours a video magenta, an image yellow, an archive red.

The colourschemes were not the problem — all four are faithful ports and have had the rules from
the start:

    if (context.Has(ContextKey.Media))
        foreground = context.Has(ContextKey.Image) ? Color.Yellow : Color.Magenta;
    if (context.Has(ContextKey.Container))
        foreground = Color.Red;

The context keys existed too, generated from ranger's own `context.py`. **Nothing ever produced
them.** Exactly the shape of the missing tag marker: styling waiting for a caller that did not
exist, so nothing failed and no test noticed, because the render tests assert the *names* on each
row.

Ranger builds a row's colour list as

    this_color = base_color + list(drawn.mimetype_tuple) + self._draw_directory_color(...)

and `mimetype_tuple` (`container/fsobject.py:239-241`) is the subset of
`('video','audio','image','media','document','container')` that holds. `FsNode.Category` is that,
worked out once per node and cached. Everything comes from the **extension** — no syscall, nothing
read from the file — which is what makes it cheap enough to colour a listing by, and is why an mp3
with an odd header is still magenta. Two fixed extension lists decide archives and documents; they
are ranger's judgement rather than anything canonical (`.gz` is an archive, `.md` a document) and
matching them exactly is the whole point.

The `.part` rule is worth keeping: ranger strips that extension before classifying, so a
half-downloaded film is still coloured as a film.

`MimeTypes` moved from `Canger.Rifle` to `Canger.Core.Model`, since `Canger.Core` cannot reference
`Canger.Rifle` (Rifle depends on Core) and a mime table is a domain concern rather than a launcher
one. Rifle still uses it from there.

**A divergence found while verifying:** the row context set `error` for an entry whose metadata
could not be read, and in the colourscheme `error` paints the *background* red. So every broken
symbolic link had a red block behind it. Ranger's per-row context is exactly selected / marked /
tagged / directory-or-file / executable / fifo / socket / device / cut-or-copied / link+good-or-bad
(`browsercolumn.py:517-551`) — no `error`, which it reserves for the whole-column "not accessible"
notice. Removed. A broken link is plain magenta, as it is in ranger.

Verified by running ranger 1.9.4 from `ranger-master/` under a pty beside Canger on the same tree
and comparing the SGR sequence in front of every entry:

    entry            ranger    canger
    adir             94        1;94      directory, bright blue
    script.sh        92        1;92      executable, bright green
    movie.mkv        35        35        video, magenta
    song.mp3         35        35        audio, magenta
    photo.png        33        33        image, yellow
    bundle.tar.gz    31        31        archive, red
    apipe            33        33        fifo, yellow
    goodlink.txt     36        36        link that resolves, cyan
    badlink.txt      35        35        link that does not, magenta
    notes.txt        0         0         document — no colour in this scheme
    README           0         0

Identical but for one thing: where ranger's colourscheme says `attr |= bold; fg = blue; fg +=
BRIGHT`, ncurses folds the bold into the bright colour and emits `94` alone, while Canger emits
`1;94`. Canger is doing what the colourscheme asks; ncurses is dropping it. On a bright colour the
difference is at most a font weight — worth knowing if directories look heavier here than in
ranger, and a one-line change to match if so.

Startup went from ~290ms to ~310ms, which is the mime table being read from `/etc/mime.types`
once on the first frame. `FileCategoryTests` pins the classification, including the `.part` rule
and the exact sizes of the three lists.

### The bookmark popup was a status line, and hints were shown instead of it

Pressing <c>'</c> showed the *hint* window — a list of the keys that could follow, including
`<bg>` and `<any>` — with every bookmark crammed onto the bottom row, shortened to fit. Ranger
shows a proper list: one bookmark per row, under an underlined `mark  path` heading.

Three separate things were wrong.

**`draw_bookmarks` was a one-line notification.** It joined every bookmark with two spaces and
abbreviated `$HOME` to `~` so they would fit on the status line. With fourteen bookmarks and long
paths that is unreadable. It now sets a flag and a real widget draws the list, laid out exactly as
`gui/widgets/view_base.py:67-92` does: bottom-anchored, `" " + key + "   " + path` per row.

**Bookmarks have to suppress the hints.** Ranger's `draw()` is

    if self.draw_bookmarks:   self._draw_bookmarks()
    elif self.draw_hints:     self._draw_hints()

so a pending `'` shows where the bookmarks lead rather than a list of the keys that could follow.
Canger drew the hints and left the bookmarks to the status line, which answered a question nobody
had asked.

**The rule under the heading is the heading, underlined.** Ranger calls
`self.win.chgat(ystart - 1, 0, curses.A_UNDERLINE)` — it does not draw a row of dashes. Canger
styled the heading with the `title` context and no underline, so there was no line at all. Both
popups now add `CellAttributes.Underline`; verified that both programs emit the same `\x1b[0;4m`
in front of the heading.

**The flag's lifecycle is the subtle part, and the order matters.** Ranger hides the bookmarks on
*every* keystroke and does it **before** running the keystroke's command (`gui/ui.py:210-214`):

    keybuffer.add(key)
    self.fm.hide_bookmarks()
    self.browser.draw_hints = not keybuffer.finished_parsing and ...
    if keybuffer.result is not None: self.fm.execute_console(keybuffer.result, ...)

Clearing it afterwards would undo the one thing the keystroke was for, because a `<bg>` binding's
whole job is to turn it back on. So: press `'` → bookmarks hidden → hints would be on → the `<bg>`
leaf yields `draw_bookmarks` → runs → bookmarks on again → `draw()` shows them and skips the
hints. Press `a` → hidden again → sequence finished, so no hints → `enter_bookmark a` runs.

**What `<bg>` is**, since it came up: ranger's `PASSIVE_ACTION` sentinel, keycode 9002. It is not a
key anyone presses. `map '<bg> draw_bookmarks` means "while `'` is pending, run this and keep
listening" — it is the mechanism by which the popup appears at all. It only appeared in Canger's
hint list because the hint window was being shown where the bookmark list should have been; ranger
lists it too in the rare case where a prefix has a `<bg>` binding and hints are showing anyway.

One deliberate difference: a path too wide for the window is cut with a trailing `~`, as every
other truncating column in Canger does. Ranger's `addnstr` cuts silently, leaving no sign that
what is shown is not the whole path.

Separately, the bookmarks file itself: `~/.local/share/canger/bookmarks` held three leftovers from
my own testing while the real fourteen were still only in `~/.local/share/ranger/bookmarks`. The
format is identical (`key:path`), so it copied straight across; the old file is kept as
`bookmarks.testing-leftovers`.

### The console history was never saved

`~/.local/share/canger/` held `bookmarks` and `tagged` but no `history`. Both settings that govern
it — `save_console_history` and `max_console_history_size` — existed and were read, and neither did
anything: nothing ever opened a file, so every command typed at `:` was lost on exit. The capacity
was fixed at fifty in the widget regardless of the setting, so raising it did nothing either.

`ConsoleHistoryFile` reads and writes ranger's own file, same name and same format — one command
per line, oldest first — so the two programs can be pointed at each other's. Loaded after the
browser is built, saved from `Dispose`.

Ranger's semantics, both worth keeping:

- **Loading is not gated on `save_console_history`.** Ranger reads whatever is there and lets the
  setting decide only whether the session writes back (`gui/widgets/console.py:79`), so turning
  saving off freezes the file rather than hiding it.
- **Only executed commands are saved.** Ranger keeps two copies — a working `history` that
  browsing edits with `modify`, and a `history_backup` holding what was really run — and saves the
  backup (`console.py:281-283`). Canger needs only one, because browsing moves a position instead
  of modifying entries, and `Add` is called from `Accept()` alone. Worth recording, because saving
  the wrong one of ranger's two would persist half-edited lines.

Written to `history.new` and moved into place, unlike ranger, which writes in place — a session
killed partway through would otherwise leave a truncated history.

Verified in a pty: three commands in one session, quit, reopen, they are there and a fourth is
appended; `--clean` leaves the file untouched; `set save_console_history false` freezes it; and
Up at the prompt offers the most recent entry rather than the oldest.

Their ranger history (50 entries) copied straight across, as the bookmarks did.

### Parity re-established against the live config, and two bugs it exposed

`ranger_settings/` had been renamed `ranger-settings/`, and its contents are byte-identical to
`~/.config/ranger/` — it is a **live copy** he edits, not a sample. It had drifted since the port:

- A dozen tools moved to `-tui` wrappers: `image-convert-tui`, `video-convert-audio-tui`,
  `media-combine-tui`, `mkv-extract-track-tui`, `audio-add-music-tui`, `audio-process-tui`,
  `media-length-tag-tui`, `file-rename-extension-tui`, `pdf-split-tui`, `text-split-tui`,
  `audio-convert-foss-tui`, `password-*-tui`, `otp-copy-tui`, `media-split-equal-tui`.
- Three commands grew a **two-branch** shape — `audio_convert_foss`, `pdf_split`, `text_split` —
  where a typed argument goes to the CLI and no argument goes to the TUI wrapper that asks. The
  bindings changed with them (`map eps pdf_split` lost its `1`).
- `gpg_signature_verify` gained a `.sig` filter.
- `file_copy_similar` gained `--preserve=timestamps`; so did `map pb`.

`cc.conf` is now `rc.conf` verbatim — Canger's DSL is the same, so there is nothing to translate
and nothing to drift. All 48 commands regenerated and reassembled; 29 generated, 19 hand-written.

**The generator refused one more shape, and had to.** `gpg_signature_verify` runs its tool per
file *and* filters which files:

    for f in files:
        if os.path.splitext(f.relative_path)[1] == ".sig":
            self.fm.execute_console(...)

It was being emitted as a bare loop, so it would have run `gpg --verify` on every selected file
rather than only the signatures. The generator now refuses a call that sits inside a conditional
*within* a loop. Deliberately only within a loop: a conditional outside one is always the
`if not cwd or not cf: notify; return` guard, and refusing those would lose six correct commands.

**Ranger's `eval` chmod loop is now translated.** Ten lines of

    eval for arg in "rwxXst": cmd("map +u{0} shell -f chmod u+{0} %s".format(arg))

produced ten startup warnings and no bindings. It is the only `eval` in ranger's *own* rc.conf, so
every derived configuration has it, and Canger's written-out equivalent was not even the same: its
bare `+r` ran `chmod +r` where ranger's runs `chmod u+r`. `RangerEvalTranslator.ExpandLoop`
expands it — a fixed string, one substitution, no evaluation — and refuses anything else.

Two real bugs surfaced from spot-checking the bindings.

**`shell -f` and `shell -t` crashed with "Object reference not set to an instance of an object".**
Every forked binding in the configuration — the chmod family, the `yf*` template copies,
`fontpreview-ueberzug`, `directory-number`, the gpg commands — did nothing but show that. The cause
is a C# record-struct trap: primary-constructor defaults do **not** apply to `new T()`, which
zero-initialises. `ProcessResult` declared `string Output = ""`, the fork and new-terminal paths
returned `new ProcessResult()`, and `Output` was therefore null where the caller did
`result.Output.Length`. Rewritten with an explicit constructor and a normalising property, so
`Output` cannot be null however the value is made and an empty one compares equal to a default one.
Worth remembering as a shape, not just a fix: any `record struct` with a defaulted parameter has it.

**`~` was expanded only by `:cd`.** `tab_new` took it literally, so all thirteen `b*` bindings —
`eval fm.tab_new(narg='Bin', path="~/Productivity_System/04 BIN")` — opened a tab on a directory
named `~` beneath the current one, showing "not accessible". Ranger expands inside `Tab` itself
(`core/tab.py:25`), which covers every caller at once; `UserPath.Expand` now does the same and
`:cd` delegates to it rather than keeping its own copy.

**Verified.** All 64 external tools named by `cc.conf` and `commands.cs` are on PATH. Every browser
binding resolves except `zi`, whose alias is created in a plugin hook that runs after the report.
Ten representative bindings driven in a pty: `ff`, `.i`, `Mi`, `oM`, `dc`, `+ur`, `bb`, `v`, `zh`,
`..` — and `yfn` genuinely copied `notes.md` out of `~/Templates`.

**Plugins.** All four accounted for: `zoxide` and `ranger-archives` ported to
`~/.config/canger/plugins/`; `ranger_devicons` is built in, and its three tables match his file
exactly (88 exact names, 227 extensions, 15 directory names — only the locale translations are
absent, which do not apply to en_IN); `ranger_udisk_menu` needs nothing, because the
`from ranger_udisk_menu.mounter import mount` at the top of his `commands.py` is **shadowed** by
his own `class mount` further down and has never run.

### Performance: measured first, and the headline hypothesis was wrong

`TODO.md` used to say *"Nothing is known to be slow; nothing has been measured either."* It has now
been measured, with `tools/measure.py` — a committed pty harness rather than a scratch script, so
the numbers are reproducible.

**The build was not the problem, though it looked like the whole story.** `canger.sh` pointed at
`bin/Debug`, `build.sh` never passed `-c Release`, and only a Debug build had ever existed — so
every number ever quoted for Canger was unoptimised. `./build.sh publish` now produces a
Release + ReadyToRun build and the launcher prefers it, warning on stderr when it falls back to
Debug. But the win is modest, not transformative:

| | Debug | Release + R2R |
|---|---|---|
| `--version` (runtime floor) | 30.8 ms | 28.6 ms |
| `--man` (registry + config + plugins) | 194.7 ms | 170.8 ms |
| first paint, small directory | ~200 ms | ~135 ms |
| **cursor move** | **~10 ms** | **~5.5 ms** |

ReadyToRun was verified applied (`RTR` marker present, Canger.Core 326 KB → 798 KB), so that 12%
on startup is real and is all it is worth. Cursor movement is the one place it nearly halves.
NativeAOT stays impossible — Roslyn needs a JIT — and `PublishSingleFile` is rejected because it
relocates `AppContext.BaseDirectory`, which is how the shipped `config/` is found.

**Two measurement traps, both of which produced a false reading before being caught.**

The first nearly went into a report: with previews on, a 20,546-entry directory measured *faster*
to start (164 ms) than a 5-entry one (270 ms). Impossible, and the cause is that previews are
generated on a worker and then request a redraw, so "the screen went quiet" was waiting on a
`bash scope.sh` subprocess. `measure.py` therefore reports **first paint and settled separately**,
turns previews off unless asked, and unsets `DISPLAY` so no ueberzug process starts. It also
refuses to average a run whose output contains no border glyph — a harness that cannot prove it
saw a frame will happily time a crash.

The second is that this laptop drifts ±50 ms between runs, enough to swamp every change made here.
Only **interleaved within-run A/B** is trustworthy, and that is how both wins below are stated.

**First paint is ~135 ms and barely depends on directory size** — 20,546 entries paint as fast as
five. What differs is the settle: ~190 ms small versus ~430 ms large. Two measured causes, both
fixed.

**`automatically_count_files` cost 70 ms of that, for forty integers.** `EnsureCounted` obtained a
directory's entry count from `ListDirectory`, which stats every entry inside it, and then kept only
`.Count`. `IFileSystem` gained `CountEntries`, an enumeration with no per-entry stat — one
`getdents` where there were thousands of `statx`. Interleaved A/B: **70 ms → 29 ms.** Kept separate
from `ListDirectory` rather than made a flag on it, because `DirectorySize` and the copy engine
genuinely need every entry's metadata.

**The redundant filter pass cost 21 ms.** `DirectoryNode.Load` filters with the field-default
pattern and then `ApplySettingsToDirectory` assigns the user's different `hidden_filter`, filtering
again. Rather than restructure the settings flow, the pass itself was made cheap: the pattern is
compiled to a `Regex` instance when assigned instead of going through the static
`Regex.IsMatch` cache probe per component per entry, and `IsHidden` walks path components as spans
instead of allocating a `string[]` per entry per pass. Interleaved A/B: **21 ms → 2.5 ms**, and it
helps every directory and every refilter, not just the large one.

Deliberately **not** `RegexOptions.Compiled`: that costs about a millisecond of codegen per
pattern, which a listing filtered once or twice never earns back.

That change also introduced and then caught a real bug worth recording: `_hiddenPattern` has a
field initialiser but the new `_hiddenRegex` did not, so a freshly constructed `DirectoryNode`
stopped hiding dotfiles until something assigned the pattern. **All 1188 tests passed with that
bug**, because nothing covered "a directory nobody configured still hides dotfiles". The two fields
are now derived from one shared builder, and three tests cover it.

**Per-frame waste removed** — small, local, and on the keystroke path where it is still felt:
`Browser` constructed a whole colourscheme object and its empty style-memo dictionary every frame
purely to compare its *name*; it called `statvfs` every frame, ungated by
`display_free_space_in_status_bar` and uncached, now behind a 2-second reading; `ScreenBuffer.Flush`
allocated a fresh `Cell[Width*Height]` (~96 KB at 120×40) on every flush, now `Array.Copy` into the
buffer it already had; and `StatusBar.DrawRight` walked `MarkedEntries` four times in one method,
now once.

### Measured and deliberately not acted on

- **Parallelising the directory scan.** The stat walk over 20,546 entries takes 59 ms serial and
  **83–92 ms across 4, 8 or 16 threads** — worse at every degree. The syscalls are dentry-cache
  served at ~3 µs and dispatch overhead dominates. The comment in `LocalFileSystem` inviting a
  caller to parallelise it is wrong and should be corrected if that file is touched again.
- **`lstat`-first.** Already done, and already matching ranger (`container/directory.py:405-412`):
  one `statx` per entry, a second only for symlinks. Nothing to win.
- **`NaturalSortKey`'s double split.** Suspected as a cost at 20k entries; `set sort basename`
  measures *slower* than natural (395 ms vs 368 ms), so the natural key is not the bottleneck.
  Left alone.
- **The 0 ms input timeout while tasks are queued.** Looks like a busy-spin, but ranger does the
  same thing — `set_load_mode(True)` → `win.nodelay(1)` (`gui/ui.py:160-171`). Parity, not a bug.
- **Deferring `stat` to visible rows via `getdents` `d_type`** (59 ms → ~20 ms on one directory).
  Cross-cutting: `FsNode.Status` is read all over the model, would need lazy filling with a lock
  because `FileStatus` is a struct and a nullable field can tear, and every sort key except the
  name-based ones still needs a full walk. A large change for a sub-frame win on one directory,
  after the two fixes above already took ~60 ms off it. Deferred, not rejected.

### Found while measuring, not performance — worth fixing on merit

- **`PreviewCache` is mutated from two threads with no lock.** `Store` runs on a preview worker
  while `Find` both reads *and* writes (`_entries[key] = entry with { LastUsed = ++_clock }`) on
  the drawing thread, and `Trim` enumerates while removing. Concurrent `Dictionary` mutation plus a
  non-atomic shared counter — a latent crash, not a slowdown.
- **Nothing bounds or cancels preview and `file(1)` work.** `ScriptPreviewProvider` and
  `FileDescriber` each `Task.Run` per item with no degree limit, and nothing cancels a request when
  its row scrolls away, so scrolling a directory of videos leaves every `ffmpegthumbnailer` running
  to its 10-second timeout.
- **`DirectorySize` recurses into its own `Parallel.ForEach`**, so the effective fan-out is up to
  8^depth rather than 8.
- **`dc` freezes the interface**: `get_cumulative_size` runs the walk synchronously in `Execute`
  instead of on the `TaskQueue`.
- **One copy step is one whole file**, with the 30 ms slice checked only between steps, so copying
  a single large file blocks input and drawing for its duration.
- **`Terminal.Resized` sets neither redraw flag.** Harmless only because every frame is currently
  drawn unconditionally — it becomes a live bug the moment drawing is gated on a dirty flag.
- **The eight `setinregex sort mtime` rules appear to do nothing.** `SettingsStore.Get` consults
  the path-scoped table only when passed a path, and `ApplySettingsToDirectory` pushes the global
  `Settings.Sort` onto every directory. Needs confirming, then filing.

### Three reports from daily use: a stale cursor, a listing that never refreshed, and one bad config line

**`dmt` then trying to enter the target printed `xdg-open: unexpected option '--'`.** Two
independent faults stacked, which is why the symptom pointed nowhere near the cause.

The real bug: a `Tab` keeps its **own** cursor, adopted from the directory's only when it enters or
is activated. Nothing copied it back after a *reload*. So `mkdirmv` moved the file away, the
listing was re-read, the directory's own cursor reconciled correctly onto the new folder — and the
tab went on holding the file that had just left. `Tab.Selected` returned a node that was no longer
in the listing, so `l` asked rifle to *open a vanished file* instead of entering the directory that
had replaced it. The status bar gave it away: `-rw-rw-r-- 0 B` beside a listing whose only entry
was a directory.

Ranger has the same tab/directory cursor split and solves it with a self-healing property:
`Tab.pointer`'s getter checks the cached pointer against the live listing on every read and
re-binds it (`core/tab.py:59-68`). Canger now does the same, but gated on a revision counter that
`DirectoryNode` bumps whenever the visible list is rebuilt — so the listing is only searched when
it has actually changed, rather than on every read. Every place the tab adopts the directory's
cursor records the revision, so the check stays a no-op until something really moves.

**Then `xdg-open --`, which is a genuine bug but not Canger's.** Their `rifle.conf:288` reads
`xdg-open -- "$@"`; ranger's shipped file (`config/rifle.conf:297`) reads `xdg-open "$@"` with no
`--`, so this is a local edit. `xdg-open` does not accept `--` and refuses outright. The `--` is
also unnecessary: rifle hands the command to the shell as `set -- 'file1' 'file2'; command`
(`RifleLauncher.cs:159`), so `"$@"` is already safe against a filename beginning with a dash —
which is the only thing `--` would have bought. Fixed in `~/.config/canger/rifle.conf` (previous
copy kept as `rifle.conf.prev`); the same line is still in `~/.config/ranger/rifle.conf` and will
misbehave there too. Line 289's `open -- "$@"` is left alone: `open` is macOS, so `has open` never
matches on this machine.

**A listing was only ever re-read when Canger itself finished doing something.** That is the other
two reports, and it is one missing mechanism. `shell -f cp …` (`yfn`) forks, so `RunProgram`'s
reload runs *before* `cp` has created anything, and then nothing looked again — the new
`notes.md` stayed invisible indefinitely. Same for a file arriving from another directory.

Ranger re-stats each visible directory from the draw path and reloads when its mtime moved:
`load_content_if_outdated` (`container/directory.py:686-711`), called from
`gui/widgets/view_miller.py:98` and `gui/widgets/browsercolumn.py:188`. Canger had no equivalent at
all. `DirectoryNode.LoadIfOutdated` now records the directory's own mtime at load time and compares
it — one `statx` — and both views call it per column per frame instead of only loading when
unloaded.

Measured: an externally created file now appears within **`idle_delay`** — ~1.9s with their
`idle_delay 2000`, and 401ms with `idle_delay 500` — where before it never appeared unaided. Any
keystroke draws a frame and therefore picks it up at once, so in practice it is immediate whenever
the user is doing anything. That bound is ranger's too; `idle_delay` is the knob.

Flat mode is deliberately excluded from the check. Ranger compares the maximum mtime across every
folded level, which means walking the tree on every draw; on a flattened 20,546-entry directory
that would cost far more than the staleness it saves. A flat listing is refreshed with
`:reload_cwd`.

No measurable cost: three extra `statx` per frame changed nothing — cursor moves stayed at
5-6ms and startup at ~145ms first paint.

`StaleListingTests` covers both faults: a file appearing from outside, an unchanged directory not
being re-read, the flat exclusion, and the cursor cases — entry removed, entry moved within the
listing, listing become empty. Neither fault had any coverage before.

### Two display gaps against ranger: where `dc` puts its answer, and the cursor in the other columns

**`dc` announced the size on the status line.** Ranger puts it in the measured row's own size
column, by overwriting that row's infostring — `look_up_cumulative_size`
(`container/directory.py:582-585`), which also prefixes `-> ` for a symlink. The answer belongs
beside the directory it is about, where the entry count was. `FsNode.CumulativeSize` was already
being set by the command and simply nothing read it: `LinemodeText.Size` now prefers it over the
entry count. The status line is still used when several things were measured at once, because no
single row carries that total.

**The cursor was drawn only in the main column.** `BrowserColumn` gated it on `IsMainColumn`;
ranger's `_get_index_of_selected_file` returns the column's own directory pointer whichever column
it is (`browsercolumn.py:453-456`), and the colourscheme reverses on `selected` regardless. So in
ranger the ancestry column shows which entry you are inside and the preview column shows where you
left the cursor. One condition removed.

**But that exposed a deeper divergence, which is what the report was really about.** With the
cursor drawn, the preview column highlighted the *first* row of the directory just left rather than
the row the cursor was on. `DirectoryNode.Create` builds a **fresh** `DirectoryNode` for every
directory entry it finds, so the node sitting in a parent's listing is a different object from the
interned one the tab uses when it enters that directory — different cursor, different marks,
different counted state. `MillerView` drew the preview column from `tab.Selected`, i.e. from the
listing, so it was drawing a node whose cursor had never moved.

Ranger has no such split: its scan interns every directory through `fm.get_directory`
(`container/directory.py:428`), so the listing holds the same object as everything else. Canger's
`DirectoryCache` interns only what tabs and pathways ask for. Rather than rework node creation —
`DirectoryNode` would need the cache that creates it — `Tab.SelectedDirectory` returns the interned
node for the selected entry and the preview column uses that. `DirectoryCache.Get` returns an
existing node with no syscalls, so this is free on every frame after the first.

Worth recording that the wider divergence remains: entry nodes are still not interned, so anything
else reading a subdirectory's state *through a listing* rather than through the cache sees a fresh
object. Nothing else currently does.

Verified in a pty by checking the reverse-video attribute rather than the reconstructed screen,
since the decoder strips colour: entering `sub`, moving to the second entry and going back up now
highlights that second entry in the preview column, a fresh start highlights the first, and the
ancestry column highlights the entry the user is inside. `dc` shows `4.5 K` in the row's size
column where the entry count was, and says nothing on the status line.

### `dc`'s three states, and the interning that had to come first

Ranger shows a directory's entry count until it is measured, then the measured size, then — once
the listing has been re-read — the same size with a `?` against it; `reset` puts the count back.
All three now work here.

**The `?` sits between the number and the unit**, not after the whole thing: `4.5? K`. Ranger gets
that by handing its formatter a different separator, `human_readable(size, separator='? ')`
(`container/directory.py:385`, `ext/human_readable.py:52`), so `HumanReadable.Format` takes one
too.

**It means "I can no longer vouch for this", not "this changed".** Ranger marks it on *any* reload
of a measured directory, and its own comment says why (`container/directory.py:379-381`): *"this is
not the first time loading. So I can't really be sure if the size has changed and I'll add a
'?'."* Finding out for certain costs exactly as much as measuring again — which is what
`autoupdate_cumulative_size` does instead, and is off by default in both.

**`reset` did not exist.** `map <C-r> reset` ships in ranger's configuration and in Canger's, and
`reset` resolved by unambiguous-prefix abbreviation to `reset_previews` — so Ctrl-R quietly did a
fraction of its job for as long as the binding has existed. Ranger's (`core/actions.py:64-77`)
drops the preview cache, discards every cached directory with `garbage_collect(-1)`, re-enters
where it was and returns to normal mode. Dropping the directories is exactly what makes a measured
size disappear, because the fresh object has never been measured.

**Directory entries are now interned, which the `?` could not work without.** `DirectoryNode.Create`
built a *fresh* `DirectoryNode` for every directory it found, so the row in a parent's listing and
the directory reached by entering it were two objects with two cursors, two mark sets and two
measured sizes. `dc` wrote the size onto the listing's copy; the reload that should mark it stale
happened to the interned one. The marker could never have appeared.

Ranger has no such split — its scan interns through `fm.get_directory`
(`container/directory.py:428`). `DirectoryCache.Intern` now does the same, reusing the statuses the
scan already read rather than re-statting, and `DirectoryNode` takes an optional cache so a node
built on its own (every test does) still behaves as before. This also completes the fix recorded in
the previous entry, where only the preview column had been routed through the cache.

Worth knowing: interning does not create more objects — ranger allocates a Directory per entry too
— it keeps them alive after the parent listing is rebuilt. Ranger's periodic collection is
**commented out** (`core/fm.py:533`), so it grows there too and is only cleared by `reset`.
`DirectoryCache.Trim` exists here and is still called by nothing; `reset` clearing the cache is now
the same escape hatch ranger has.

Verified in a pty, reading the reconstructed screen rather than the raw stream, because the row's
name and its size are written as separately positioned pieces and searching the stream for
"sub … 4.5 K" finds nothing even when the screen shows exactly that:

    fresh              0   sub        1
    after dc           0   sub    4.5 K
    folder changed     0   sub   4.5? K
    after Ctrl-R       0   sub        2

### A false regression, and the control that now prevents one

Straight after the interning change the numbers looked awful: first paint 140ms → 230ms, cursor
5.5ms → 7.6ms, and the large directory reporting no frame at all. Two separate things, neither of
them the change.

**The machine was busy.** `--version` — which parses arguments and exits, and touches none of this
— had gone from 28.6ms to 52.9ms on its own. `mpv`, VS Code and Teams were running. Measured
against that control, first paint was 4.1x where it had been 4.9x before: relatively *better*.
`tools/measure.py` now measures `--version` in the same run and reports every case as a multiple of
it, because that ratio is the only figure comparable between runs on a laptop.

**And the settle detector was stopping too early.** Canger emits the alternate-screen escapes, then
goes quiet for as long as the directory takes to scan. On a loaded machine with 20,546 entries that
gap exceeded the 350ms quiet threshold, so the harness timed the escapes and stopped — 26 bytes,
no frame. It now refuses to treat a gap as the end until the output actually contains a laid-out
frame. The `laid_out` check added earlier is what caught this rather than letting a 209ms
"startup" be reported as an improvement.

### Pasting into the console did nothing

`:cd` then Ctrl-Shift-V put nothing in the prompt. Canger decoded the paste correctly and then
threw it away: `Browser.Handle` opened with

    if (evt is not KeyEvent key) { continue; }

so every `PasteEvent` was discarded. The decoder built one, the event type documented what it was
for, `ConsoleWidget.Insert(string)` existed to receive it — and nothing connected them.

**Why it works in ranger and not here.** Ranger never enables bracketed paste — there is no `2004`
anywhere in its source. So the terminal delivers a paste as ordinary key presses and the console
consumes them one at a time, with no paste handling needed. Canger sends `?2004h`
(`Ansi.EnableBracketedPaste`), which asks the terminal to wrap a paste in markers and deliver it as
one lump. Having asked for that and then ignored it, pasting silently did nothing.

Keeping bracketed paste is the better of the two, and the event type already said why: in ranger a
paste into the *browser* runs whatever bindings the characters happen to name, so a pasted `q`
quits and a pasted newline submits a half-finished command. Canger now ignores a paste outside the
prompt and says so on the status line, rather than executing it. Verified: pasting `q` into the
browser leaves it running.

Newlines become spaces rather than submitting the line. A prompt holds one line and a clipboard
entry ending in a newline is very common; running a half-read command because of a trailing
newline is exactly what bracketed paste exists to prevent. Verified end to end by writing the
byte sequence a terminal actually sends — `ESC[200~<text>ESC[201~` — into the pty: the path lands
in `:cd`, Enter navigates into it, and a two-line paste arrives as `line one line two` without
executing.

### `q` quit the program instead of closing a tab

All four quit commands were the same line — `FileManager.Quit()` — so `q`, `q!`, `Q` and `Q!` did
exactly one thing between them. `quit`'s own summary already said what it was supposed to do,
*"Close the current tab, or quit when it is the last one"*, and the code did not do it.

Ranger's rules (`config/commands.py:654-710`), now matched:

| command | with 2+ tabs | with one tab |
|---|---|---|
| `quit` (`q`, `ZZ`, `ZQ`) | closes the tab | quits, unless work is in progress |
| `quit!` | closes the tab | quits regardless |
| `quitall` (`Q`) | quits, unless work is in progress | same |
| `quitall!` | quits regardless | same |

Note that the forcing form forces the *quit*, not the tab handling: `quit!` with two tabs still
closes a tab. That reads oddly until you see that `!` in ranger only ever means "do not stop me
over unfinished work".

**The second half was a silent data hazard.** Ranger refuses to quit while its loader has work and
names the way round it; Canger quit regardless, abandoning a copy midway without a word. It now
says exactly what ranger says: `Not quitting: Tasks in progress: Use `quit!` to force quit`.
Verified against a real 700 MB copy — `q` during it is refused, and the message appears.

Verified in a pty: `<C-n>` opens a second tab, `q` closes it and Canger keeps running, a second
`q` then quits; `q` with one tab quits; `Q` with two tabs open quits outright.

**On "stuck in the terminal": not reproduced, and the teardown looks correct.** Measured on exit,
Canger leaves the alternate screen (`?1049l`), shows the cursor (`?25h`), disables bracketed paste
(`?2004l`) and the process is reaped in well under a tenth of a second. The disposal order is also
right — the browser, then the image display, then the terminal — so an ueberzug overlay is torn
down *before* the alternate screen is left rather than after. The most likely explanation is simply
that `q` quitting unexpectedly is what "gets closed" describes. If it recurs, the thing to capture
is whether the shell prompt returns at all, or returns with the terminal in a strange state, since
those point at different causes.

### Closing a tab lands on the neighbour, not on tab 1

`q` on tab 5 of five closed tab 5 and then jumped to **tab 1**. `Browser.CloseTab` picked the
successor with `_tabs.Keys.Order().First()` — always the lowest-numbered surviving tab.

Ranger states the rule the other way round (`core/actions.py:1283-1288`): it moves off the tab
*before* deleting it, `tab_move(-1)` when the tab is last in the sorted list and `tab_move(+1)`
otherwise. In terms of the tabs that survive, that is "the next one, or the previous one when
there is no next" — which is what `Browser.NeighbourOf` now computes.

- Closing 5 of {1,2,3,4,5} → 4. Holding `q` counts down 5, 4, 3, 2 instead of bouncing to 1.
- Closing 3 of {1,2,3,4,5} → 4.
- Closing 1 → 2.
- Numbering need not be contiguous (`:tab_new 7`, `renumber_tabs_on_tab_close false`), so the
  neighbour is the nearest surviving number rather than `closed ± 1`.

`RenumberTabs` already follows the current tab by position, so it composes with this unchanged.

Verified in a pty at 130x30 by reading the tab strip out of the reconstructed title bar: five
tabs, `q` → `1 2 3 4` with 4 current; `q q` → `1 2 3` with 3 current; stepping back to tab 3
first and then `q` → lands on 4. `Browser` needs a real `Terminal` to construct, so the decision
itself is unit-tested as a pure function — `Canger.Ui` now has `InternalsVisibleTo`, following
`Canger.Core`'s convention. `tests/Canger.Ui.Tests/TabClosingTests.cs`, 6 tests.

### Tabs are named after their directory (`dirname_in_tabs`)

`bo` and `bk` showed bare numbers where ranger shows `2:Books`. Two separate things were missing,
both the same shape as the tag marker: the setting existed, was typed, was parsed, was readable
through `CangerSettings` — and nothing anywhere consumed it.

**`dirname_in_tabs`.** `TitleBar` took an `IReadOnlyList<int>` of tab numbers, so it could not
have drawn a name even had it wanted to. It now takes `TabHeading(Number, Path, Label)` and
applies ranger's rule (`gui/widgets/titlebar.py:151-161`): a leading space and the number, then
`:name`, where the root's empty basename is written `:/` and a name over fifteen is cut to
fourteen plus an ellipsis. The leading space is the separator, so there is no trailing one —
Canger had `" {n} "`, which put the active tab's highlight one cell wider than ranger's.

The cut is by display *width* rather than character count, which is where this parts company with
ranger deliberately: it is the terminal's cells that run out, and nine CJK characters are eighteen
cells wide though Python's `len` calls them nine. For ASCII the two agree exactly.

`unicode_ellipsis` is now honoured here. It is still dead in `BrowserColumn`, which truncates
filenames with a hard-coded `~` — and ranger also preserves the extension across that truncation
(`browsercolumn.py:462-477`), which Canger does not. Both worth fixing; neither was asked for.

**Tab labels.** `map bk eval fm.tab_new(narg='Books', path="~/Books")` was already translated to
`tab_new ~/Books label=Books` and `Tab.Label` was already set — and nothing drew it either.
Ranger has no separate notion of a label: `tab_new(narg=...)` keys the tab *dictionary* by that
string (`core/actions.py:1357-1363`) and the title bar draws `str(tabname)` whatever type it is.

**The number stays in front of a label — a deliberate divergence.** Rendering ranger's result
literally gives `Books:Books`, because in ranger the label *is* the key and there is no number
left to show. Canger numbers tabs properly, and the number is what `2gt` and `<A-2>` act on, so
dropping it costs the reader the only part of the strip they act on. A labelled tab is therefore
`2:Books`, and a label displaces the *directory name* rather than the number. `dirname_in_tabs`
governs the directory name only; an explicit label is drawn either way.

Their `open_in_tabs` passes `narg=f.relative_path` too (`ranger-settings/commands.py:498`), which
is why `bo` labels its tabs at all rather than leaving them plain numbers.

**A label may contain spaces.** `tab_new` read the label through `NamedArguments()`, which splits
on whitespace, so `narg='Task Capture Bin'` arrived as `Task`. The label now runs to the end of
the line, which is what the translator emits and what ranger's quoted string means.

**The strip no longer vanishes on a narrow terminal.** `DrawRight` dropped the entire right-hand
side when it did not fit, so at 60 columns five open tabs were invisible with nothing to say they
existed. Labels are much wider than numbers, which is what made a long-standing edge case
reachable at ordinary widths. Ranger never hits it because its tab parts are `fixed=True` and its
bar shrinks the path instead (`gui/bar.py`) — but that still fails once the tabs alone are wider
than the line, which ranger simply does not handle.

Now: the tab list grows *outwards from the active tab*, so the tab you are on is the one
guaranteed to be drawn; the branch and the key buffer are dropped before any tab; and the
hostname is dropped rather than truncated when the tabs have taken the line, since writing it
anyway landed it on top of them. Tabs are offered the whole line rather than what the branch has
left of it — getting that the wrong way round dropped a tab to make room for a branch that was
then discarded anyway, which is what the new tests caught.

Verified in a pty against their real `~/.config/canger/cc.conf`:

| keys | cols | tab strip |
|---|---|---|
| `bk` | 150 | `1:manuj 2:Books` |
| `bk bz` | 150 | `1:manuj 2:Books 3:Task Capture Bin` |
| `bk bn bp` | 150 | `1:manuj 2:Books 3:NOTES 4:Projects` |
| `<Space><Space> bo` | 150 | `1:manuj 2:Backups 3:Books` |
| `<C-n><C-n>` | 150 | `1:manuj 2:manuj 3:manuj` |
| `bk bz bs br` | 80 | `1:manuj 2:Books 3:Task Capture Bin 4:Study Passive 5:RA RP SP` |
| `bk bz bs br` | 60 | `2:Books 3:Task Capture Bin 4:Study Passive 5:RA RP SP` |
| `bk bz bs br` | 45 | `4:Study Passive 5:RA RP SP` |

`tests/Canger.Ui.Tests/Widgets/TabHeadingRenderTests.cs` (15) and four more in
`TabCommandTests`. `TitleBar` renders into a `ScreenBuffer` directly, so these assert the drawn
text and the active tab's background rather than the inputs — which is the class of bug that kept
getting through.


## QA sweep — self-testing instead of being tested

Run on `feature/qa-sweep`, driving Canger in a pty through the workflows a daily driver actually
uses, plus a mechanical audit of every setting for the defect shape this port keeps producing:
*the setting is defined, typed, parsed and readable, and nothing consumes it.*

**Method.** For each of the 82 settings, resolve its `CangerSettings` property and count readers
outside `CangerSettings.cs`. Seventeen are read by string key and all seventeen are wired.
Twenty-one have a property nothing reads at all.

**What was checked and found working**, so it is not re-investigated: navigation and the scroll
clamp; all eight sort orders (`om` is mtime — `ot` is type, which cost a false alarm); `zh`;
marking with `<Space>`, `v`, `uv`; search with wrap-around; bookmarks; `yy`/`pp`, `dd`/`pp`,
`:mkdir`, `:touch`, `cw` rename; `dD` with and without the multiple-file confirmation; `:flat`;
`zf` filtering and `:filter` clearing; the task view; `?` help and its pager; console completion
for both commands and paths; console history; bookmark, tag and console-history persistence.

### Found

| | What | Impact |
|---|---|---|
| 1 | **Resizing the terminal does not redraw.** The screen keeps the old geometry — wrapped and garbled — until a key is pressed. `Terminal.Resized` sets neither redraw flag; measured 0 bytes emitted after `SIGWINCH`. | Blocks daily use in a tiled WM or tmux |
| 2 | **`update_title`, `update_tmux_title`, `shorten_title` dead.** Canger never emits an OSC title sequence, so the window title never says where you are. | Visible every session |
| 3 | **`wrap_scroll` dead.** `j` at the bottom never wraps to the top. | Visible |
| 4 | **`save_tabs_on_exit` dead**, and so `filter_dead_tabs_on_startup` with it. No `tabs` file is written and tabs do not survive a restart. Bookmarks, tags and history all persist correctly; only tabs do not. | Visible |
| 5 | **`status_bar_on_top` dead.** The bar stays at the bottom. | Layout |
| 6 | **`collapse_preview` dead.** The preview column keeps its width with nothing to show. | Layout |
| 7 | **`clear_filters_on_dir_change` dead.** A filter set in one directory follows you into the next. | Surprising |
| 8 | **`cd_bookmarks` and `cd_tab_fuzzy` dead.** `:cd` completion offers neither bookmarks nor fuzzy matching. | Console |
| 9 | **`freeze_files` dead.** No way to stop the listing reloading, and no `FROZEN` indicator. | Niche |
| 10 | **`size_in_bytes` dead**, and **`binary_size_prefix` only half-wired** — it reaches the linemode but not `StatusBar` or `BrowserColumn`, so one screen shows two renderings of the same quantity. | Visible when on |
| 11 | **`flushinput` dead.** Keys typed during a load are not discarded. | Niche |
| 12 | **`open_all_images` dead.** Opening one image does not hand the whole directory to the viewer. | Visible for media |
| 13 | **`xterm_alt_key` dead.** | Niche |
| 14 | **`bidi_support` dead.** Right-to-left names are not reordered. | Niche |
| 15 | **`w3m_delay`, `w3m_offset`, `iterm2_font_width`, `iterm2_font_height`, `sixel_dithering` dead** — but so are the backends they configure, so these follow the backends rather than lead them. | Blocked |
| 16 | **`canger --clean` still loads a plugin** (`commands-c8234704: 2 commands`), though `--clean` is documented as ignoring all configuration and plugins. | Debugging |

Two further findings came out of fixing the above rather than the sweep itself:

| | What | Impact |
|---|---|---|
| 17 | **A page of movement was a constant sixteen rows** — `scroll_offset * 2` — on every terminal, where ranger uses the browser's own height (`core/actions.py:522`). Page-down on a tall window moved a third of the way down it. | Visible |
| 18 | **`canger a b c` opened only `a`.** Ranger builds one tab per start path (`core/fm.py:127`); Canger read `Paths[0]` and dropped the rest, despite the usage line saying `[path ...]`. | Visible |

### Fixed

1, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 16, 17, 18, 19 — see the commits on `feature/qa-sweep`
and `feature/qa-sweep-2`. Fifteen of nineteen.

**Not a bug after all: the window title.** Ranger only writes one when the terminal advertises a
status line (`curses.tigetflag('hs')`, `gui/ui.py:131`). `xterm-256color` does not have it, so
ranger sets no title there either — implementing this would change nothing on the terminal it was
reported against. It remains a genuine gap on `tmux-256color` and `alacritty`, which do advertise
one, and `update_tmux_title` is a separate mechanism that would work anywhere. Both left.

### Second pass

A nineteenth finding came out of implementing the eighth: **`:cd` completed only against names in
the current directory**, so `:cd /usr/lo` and `:cd ~/Doc` did nothing at all — most of the typing
a `:cd` saves. Fixed with 6, 8, 9, 11 and 12.

One trap worth remembering from that: `UserPath.Expand` falls back to the *process's* working
directory, which is wherever Canger was started from rather than where the user is now. Relative
completion worked in a pty and failed in a test for exactly that reason.

### Still open

`cd_tab_fuzzy` — a recursive multi-token directory matcher, and off by default in ranger, so it
is opt-in rather than missing. `xterm_alt_key` (13), `bidi_support` (14), the title family (2),
and the settings that follow the unimplemented image backends (15).

### Regression sweep after the fixes

Twenty-three workflows re-driven in a pty and all correct: the listing, `G`, `zh`, marking, `uv`,
search, `yy`/`pp`, `:mkdir`, `cw`, `dD`, `:flat`, `zf`, the task view, `?`, tabs, `q` closing a
tab, `:cd` completion, bookmarks, and paging. One apparent failure was the test's own fault — it
asserted a 24-row page in a twelve-entry directory, where clamping at the end is right.

### The copy engine, tested against `cp` — and `pc`/`pm`/`pb` removed

Three bindings ran an outside program to do what `pp` does: `pb` was
`cp -rv --reflink=auto --preserve=timestamps %c %d`, and `pc`/`pm` called personal scripts whose
own headers say they exist to give ranger *"a progress bar"*. All three were workarounds for
things ranger lacks and Canger has.

Before removing them, Canger's paste was compared against `cp -rv --reflink=auto
--preserve=timestamps` on btrfs over a directory built to be awkward: a 100 MB sparse file, two
hard links to one inode, a symlink, a nested tree, and files with non-default modes and a 2020
mtime.

| | result |
|---|---|
| sparse file | 100 M apparent, **0 allocated** both sides — the reflink preserves the holes |
| hard links | become two separate inodes — **the same as `cp -rv`**, which needs `-a` or `--preserve=links` to do otherwise |
| symlink | recreated as a link, not followed |
| modes | `700` and `755` preserved |
| mtimes | preserved to the nanosecond |
| recursion, contents | identical |

`diff -r --no-dereference` between Canger's result and `cp`'s reports **no difference at all**.

**One real gap, found and fixed.** A cancelled copy left the bytes it had managed at the
destination, under the right name, with nothing to say it was a fragment. `cp` does the same on
Ctrl-C, but a file manager with a cancel key in its task view is a different proposition: the user
pressed something that says stop. `CopyEngine` now removes a partial destination on cancellation
*and* on an I/O error — but only when the copy created it, since overwriting one that already
existed has truncated it already and deleting it too would turn a damaged file into a missing one.

Testing that needed care. On this machine a same-filesystem copy is reflinked and a 700 MB
cross-filesystem copy finishes in 0.3 s, so racing it with a timer proves nothing; the test
cancels from inside the progress callback, with reflink and kernel copy disabled so there is a
mid-file moment to cancel at. `tests/Canger.Core.Tests/FileOperations/CancelledCopyTests.cs`, 4
tests, checked against a reverted fix.

**And `shell -q`.** The removed bindings were worse in Canger for a reason that outlives them: a
`shell` command takes the terminal for its whole duration, so a long conversion blanks the screen
with no sign anything is happening. `-q` — Canger's own flag, ranger has nowhere to run a shell
command but the foreground — puts it on the task queue instead: spinner, task view, cancellable,
browser still usable. Verified with `:shell -q sleep 4`, moving the cursor while it ran.

Their other long-running `shell` bindings are candidates for `-q`, but that is their config to
change rather than mine.

### Control characters were written straight to the terminal

Reported as "the popup menu is not cleanly formatted": the hint window had fragments of other
rows scattered through it at odd columns.

The cause is not the hint window. Their bindings are written with a trailing tab and a comment —
`map ecc shell -d clipboard-clear<TAB><TAB><TAB># Clear clipboard` — and ranger keeps that in the
command, since `source` skips only lines that *start* with `#` (`core/actions.py:378-381`). So
Canger stores it too, correctly. What Canger then did was write the tab out verbatim.

The buffer's contract is one cell per column, and a tab breaks it: the terminal moves to the next
tab stop without clearing what it passes over, so the row keeps whatever the listing had drawn
there and everything after lands in the wrong column. Exactly the corruption reported.

**The same hole was a good deal worse than untidy.** Nothing between a filename and the terminal
was checking, so a file named with an escape sequence had that sequence written out — enough to
recolour the screen, clear it, or move the cursor, chosen by whoever named the file rather than
by whoever is reading it. Verified before the fix: a file called `colour<ESC>[31mred.txt` put a
raw `ESC [ 3 1 m` into the output stream. Ranger has the same gap and says so
(`gui/widgets/titlebar.py:92`, *"TODO: Properly escape non-printable chars"*), so this is a
deliberate divergence.

Fixed at `ScreenBuffer.Set`, the one place every widget's text passes through. A tab becomes a
space — which is what it was standing in for, and makes those trailing comments read as the
descriptions they are — and everything else `Rune.IsControl` accepts becomes `?`, visible rather
than silent, because a name with something odd in it should look odd.

| | before | after |
|---|---|---|
| `ec` hint row | `shell -d clipboard-clear` then listing text bleeding through | `shell -d clipboard-clear   # Clear clipboard` |
| `tabbed<TAB>name.txt` | row corrupted from the tab onward | `tabbed name.txt` |
| `colour<ESC>[31mred.txt` | raw escape reaches the terminal | `colour?[31mred.txt`, nothing reaches it |

`tests/Canger.Tui.Tests/ScreenBufferControlCharacterTests.cs`, 12 tests.

### A visual selection did not say it was still open

Reported as a bug: two files selected, a third created between them, and the new one joined the
selection.

It is not a bug in the marking. Marks survive a reload by *path* — verified at the model level
and through three routes a file can appear — and plain `<Space>` marks never picked the new file
up. It happens only while visual mode is still on, and then it is the range doing what a range
does: ranger recomputes `targets` from the live listing on every move
(`core/actions.py:524-558`), so anything between the two ends is in the selection, exactly as
vim's visual mode works.

What was missing is any sign that the range was still open. Ranger shows none either — its status
bar knows about the filter, the marks, the position and frozen files, but never the mode — which
is why a selection quietly absorbing a new file reads as a defect. The status bar now says `VIS`
beside `Mrk`, and `UNVIS` for the range `uV` starts, following the `u`-means-undo convention the
bindings already use.

Considered and rejected: remembering the range as a set of files rather than a span of positions.
It would stop moving back over your own path from deselecting, which is what visual mode is for —
a rare surprise traded for a constant one. Also rejected: ending the mode when the listing changes
underneath, which would drop a selection at unpredictable moments.

| state | status bar |
|---|---|
| two plain marks | `0/2  Mrk` |
| `V` | `0/1  Mrk  VIS` |
| `V` `j` | `0/2  Mrk  VIS` |
| `V` `j` `V` | `0/2  Mrk` |
| `uV` `j` | `2/4  33%  UNVIS` |
| range open, a file appears between the ends | `0/3  Mrk  VIS` |

### Path-scoped settings were never consulted

`setinregex`, `setinpath` and `setintag` were parsed, validated, stored and reported without
error — and read by nothing. A rule saying "sort this one directory by date" did nothing at all,
silently, which is the worst way for a configuration directive to fail.

`SettingsStore.Get` resolves a path scope only when it is *given* a path, and `CangerSettings` —
the typed facade every reader in the codebase goes through — never passed one. All 66 accessors
asked for the global value. Ranger falls back to `fm.thisdir.path` inside its own lookup
(`container/settings.py:222-235`); `CangerSettings.CurrentPath` is that, as a function rather than
a value because settings are read between frames as well as during them and the answer has to be
current at the moment of the read.

This had been suspected earlier in the session — *"the eight `setinregex sort mtime` rules appear
to be inert"* — and recorded rather than chased. It was worth chasing.

Verified in a pty on the reported directory, comparing the same listing with and without the
rule: the order changes, and the `~/...` form and the absolute form produce the same order, so
the tilde is expanded correctly. A controlled directory of three files with known timestamps
gives exactly the expected order for `sort mtime` + `sort_reverse true`, and a neighbouring
directory with no rule stays alphabetical.

Cost: resolving a scope walks the rules with a compiled regex per read. With their sixteen rules
in a thousand-entry directory, key latency measures 1.3 ms median and 6.1 ms worst — no change
worth reporting. With no scoped rules the list is empty and the loop costs nothing.

`tests/Canger.Core.Tests/Settings/PathScopedSettingsTests.cs`, 8 tests.

### Status-bar messages never went away

A message sat on the status bar until the next keystroke. If the keystroke that produced it was
the last one for a while — `,` marking a set of files, then reading the screen — it stayed there
indefinitely, hiding the line about the file under the cursor.

Ranger does both things: `ui.press` clears the message on any key (`gui/ui.py:209`), *and* it
expires on its own after four seconds (`fm.notify`'s `duration=4`, `core/actions.py:165`). Canger
had only the first half.

Two details worth keeping:

- The expiry runs at the top of the frame, not inside the status bar's own branch. The loop now
  shortens its input wait while a message is showing, so a message that could never expire —
  with the console open over it, say — would leave the loop spinning on a zero timeout. Measured
  at 0.8% CPU with the console open over a message, against a spin if the check sat in the
  branch, which is where I first put it.
- The wait is capped at the time remaining. Otherwise the loop sleeps for the whole `idle_delay`
  and the message lingers up to two seconds past its four, which is half again as long as it was
  meant to be there.

Verified in a pty: shown at 0.5s, 2.0s and 3.5s; gone by 4.5s; cleared immediately by a keypress.

### Tab completion picked one match and stopped, and `:shell` offered none

Two separate faults behind the same report.

**The cycle was defeated from outside.** `CycleCompletions` is written correctly — it seeds a list
with the typed line and the candidates, then walks it. But the caller recomputes the candidates
from the console's *current* text on every Tab, and after the first Tab that text is no longer
`f` but `fd_next ` — which contains a space, so `CompletionsForCurrentLine` takes the
argument-completion branch and comes back empty. The empty list hit an early return placed
*before* the "a cycle is already running" check, so the second Tab did nothing.

The check for candidates now applies only when starting a cycle. A running one needs no
candidates: it already holds the list.

**`:shell` had no completion at all**, where ranger completes against `get_executables()`. `s` is
bound to `console shell%space`, so the whole program name had to be typed. `Executables` gains a
PATH walk, cached, and `ShellCommand.Complete` offers matching programs — keeping any flags
already typed, and offering nothing once the program is named, since past that the user is writing
a command line and every binary on the machine would be noise.

Verified in a pty against the real configuration: `:f` then Tab walks `fd_next`, `fd_prev`,
`fd_search`, `file_convert_text`, `file_copy_similar`, and Shift-Tab walks back; `s lsb` offers
`lsb_release` then `lsblk`; `:shell -w gz` offers `gzexe` then `gzip` with the `-w` intact.

Eleven tests; three fail against the old behaviour and four cover a path that had none.

### Every binding with a tab before its comment was dead

Reported: `edn`, `eft`, `cer`, `cvr`, `ctr` do nothing. Thirty-six bindings in the real
configuration were affected.

They are written `map edn directories_number_highlight<TAB><TAB><TAB># Number Highlighted
Directory`, and ranger keeps a trailing comment in the command — `source` skips only lines that
*start* with `#` (`core/actions.py:378-381`). Ranger then splits with `str.split()`, which treats
a tab as a separator, so the name comes out clean and the comment is harmless.

`CommandLine` split on `' '` alone. The tabs and the comment stayed inside the first word, so the
name was `directories_number_highlight\t\t\t#` and no such command existed. `Rest` had the same
assumption, so flags and arguments were mis-parsed for the same lines.

The `shell …` ones *appeared* to work, which is why this went unnoticed: the whole line after
`shell` is passed to `sh`, and `sh` ignores the comment itself.

**The diagnostic had been taught to agree with the bug.** `--config`'s binding check does this:

```csharp
// Split on any whitespace, not just a space: a `map` line may separate the command from a
// trailing comment with tabs, and taking "cmd\t\t#" as the name reported a great many
// perfectly good bindings as broken.
string name = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries) ...
```

So the problem *was* seen — in the report — and fixed there, in the one place that only describes
behaviour, rather than in `CommandLine`, which produces it. The check was made to agree with what
the user expected while the runtime went on disagreeing, and the thirty-six dead bindings became
invisible. Worth remembering: when a diagnostic and the code disagree, the diagnostic is the one
that must not be adjusted first.

Now split on any whitespace in both `Words` and `Rest`, matching ranger. Verified in a pty against
the real configuration: `edn` reports "Numbering directories.", and `eft`, `cer`, `cvr` and `ctr`
no longer produce `unknown command`, which is what a genuinely missing one still says.

Seven tests; five fail against the old behaviour.

### The three left over from the audit

All three fixed, on the principle that behaviour should be correct even where ranger's is not.

**A filename could substitute another file's name.** Seven copies of a shell quoter had grown up
across `src/` and the plugins, disagreeing about one thing: quoting is not enough when the result
goes into a line that `Execute` then expands, because the whole line is expanded — including the
part just quoted. A per cent is read as the start of a macro, and the substituted value's own
opening quote closes the quoting around the name, leaving the rest bare. `My%20Docs` was enough to
break such a command; `x%sy.txt` beside `;id;.txt` was enough to make it run something.

The pair is now named and provided once: `MacroExpander.ShellQuote` for a command going straight
to a runner, `QuoteForCommandLine` for a line going to `Execute`. `RenameAppendCommand` had been
doubling the per cent by hand with a comment explaining why — the trap was known and still easy to
fall into. Checked which callers actually need it rather than doubling everywhere: archives and
zoxide go through `Runner.Run` and must not, nor must rifle or the terminal runner.

**One selected file could overwrite another.** `po` is a policy about what is already at the
destination, not permission for two of the user's own files to collide — but a flattened listing
holding `sub1/a.txt` and `sub2/a.txt` resolved both to one target, and on a move the second
overwrote the first and then deleted its own source. `CopyJob` now remembers the targets it has
written and makes a repeat unique whatever the policy says; an existing file is still replaced,
which is what `po` was asked to do.

**A newline in a path corrupted the tag and bookmark files.** Both use ranger's line-per-entry
format so the files can be shared, and such a path cannot be represented: a tag became two lines
and came back as two tags on paths that do not exist. Refused rather than escaped — escaping would
fix the round trip and make the file unreadable to ranger, a poor trade when what is lost is the
tag rather than the file. The refusal is narrow, and a test pins that spaces, quotes, colons and
per cents still work.

Thirteen tests; seven fail against the old behaviour.

## Data-loss audit

A file manager that destroys data it should not is worse than no file manager. Three sweeps over
every path that removes, renames or writes over user data — destructive operations, overwrite
paths, state files and shell quoting — each finding then re-read against the source and against
ranger. Seventeen findings; ten fixed, and one of them was destroying data in ordinary use.

### The one that mattered

**A failed move deleted the source anyway.** `TransferDirectory` collects per-entry errors and
carries on — deliberately, so one unreadable file does not abandon a transfer — then ran
`DeleteRecursive(source)` without consulting them. A destination out of space, a name the
filesystem will not take, a file past FAT32's four gigabytes: the original was gone, with a line
in the task view to say so.

Ranger reaches the opposite outcome from the other side: `copytree` raises when its error list is
non-empty, which puts `rmtree(src)` out of reach (`ext/shutil_generatorized.py:277-279`,
`:318-321`). The port kept the collecting and dropped the consequence.

Counting errors per subtree rather than flagging is what makes the fix propagate: a failure three
levels down is counted at every ancestor, so none of them delete. The resulting semantics beat
ranger's — because Canger moves file by file, each file ends in exactly one place, where ranger
leaves the whole source plus a partial copy.

Verified end to end on btrfs: moving a tree containing an unreadable file to tmpfs leaves that
file at the source, moves the rest, and reports two problems.

### "Could not read" is not "empty"

The same mistake in three separate classes, each losing everything accumulated, each silently:

- `Tags.Reload` cleared before the `try`, so a read failure emptied the set and the next save
  wrote the emptiness over every tag.
- `Bookmarks.ReadFile` returned an empty dictionary on failure; the three-way merge re-adds only
  keys *changed this session*, so every untouched bookmark was dropped and the result written.
- `FileMetadata.Read` caught everything and returned empty, so one `:meta` on an unparseable
  database replaced hundreds of annotations with a single entry.

The trigger is ordinary — a state file left root-owned by one `sudo` run, any EACCES or EIO. The
directory stays writable, so the rename succeeds and nothing is reported. Ranger guards all three.
`FileMetadata` was also the last writer truncating in place, and it lives among the user's own
files; it now writes beside and replaces, which needed `IFileSystem.Replace` — an explicitly
overwriting rename, kept separate from `Rename`, which refuses an existing destination and is what
stops `:rename` and `:bulkrename` destroying a file.

### Reported honestly rather than claimed

The audit called the missing same-file guard a data-loss bug. **It is not, and I measured before
writing the fix.** .NET's advisory lock on Linux is inode-scoped, so opening one file for reading
and for truncating writing collides: a self-copy, a copy onto a hard link, and a copy onto a
symlink to the source all left the file intact. The guard went in anyway — that protection is an
implementation detail rather than a decision, it disappears if `System.IO.DisableFileLocking` is
set, and the error it produced ("used by another process") told the user nothing true. It is
hardening and a better message, not a save.

### The rest

`:edit` built `{editor} {quoted}` with no `--`, so a file called `+!rm -rf ~/Documents` was read
by vim as a command to run and `E` was enough — Canger's own `rifle.conf` carries the `--`; the
command bypassed rifle and dropped it. The containment guard lived inside `CopyJob`, so the three
linking pastes had none, and it compared paths as typed, missing a destination that reaches the
source through a symlink; both now use `PathRelation`, which resolves first. `:delete` and
`:trash` silently discarded an argument and acted on the selection instead — they refuse now,
rather than gaining a shell-splitter inside the one command that cannot be undone. `SafePath`
asked `Exists`, which follows links, so a *broken* link read as a free name and the write landed
wherever it pointed; `ExistsNoFollow` is the `lstat` to that `stat`. `CopySymbolicLink` deleted
before creating. Saving state through a symlink broke the link. `--clean` pointed tags at
`/dev/null` and relied on a rename failing, which as root it does not.

### Deliberately not done

Two findings are ranger parity and were left: `po` with two selected files sharing a basename
(overwrite invites it), and a newline in a tagged path corrupting the line-per-entry format. The
`%`-in-a-filename hazard in the personal `commands.cs` is out of scope by agreement, and recorded
in the plan with its one-line fix.

Twenty-nine tests across five files, every one checked against the unfixed code.

### File sizes were rounded to one decimal place instead of three significant figures

`19.9 M` showed as `20 M` and `7.59 M` as `7.6 M`. Ranger uses `%.3g` — three *significant
figures* (`ext/human_readable.py:51`) — while `HumanReadable.Format` rounded by decimal places:
one below ten, none above. Its own comment claimed three significant figures, which is what the
code was meant to do and never did.

Fixed, along with three smaller divergences in the same twenty lines, all visible in the
screenshot that reported it:

- **`k`, not `K`.** Ranger's decimal prefixes are `('B', 'k', 'M', 'G', 'T', 'P')` — the kilo is
  the only lowercase one.
- **Four figures at a thousand or more.** Reachable only with binary prefixes: 1023 Mi is a real
  quantity and three figures would call it 1.02 Gi. Ranger's `%.4g`, same reason.
- **Trailing zeros go**, as `%g` drops them: `1.5 k` not `1.50 k`, `20 M` not `20.0 M`, and zero
  is a bare `0` with no unit or separator (`human_readable.py:34-35`).

One deliberate difference. Rounding can carry a value up a digit — 999 999 bytes is 999.999 k,
which to three figures is 1000 — and C's `%g` switches to exponential notation when it does, so
ranger renders that as `1e+03 k`. Five characters that say less than the number they replaced, in
a column measured in characters. Canger writes `1000 k`. The band is the last 0.05% below each
unit boundary.

**The same bug existed twice.** `CopyProgress.FormatBytes` was a verbatim copy of the old rounding
logic, so a transfer running at 19.9 MB/s reported `20 M/s`. It now calls `HumanReadable.Format`
rather than carrying its own copy.

Verified against the reported screenshot, same four files:

| bytes | ranger | Canger before | Canger now |
|---|---|---|---|
| 19 926 367 | `19.9 M` | `20 M` | `19.9 M` |
| 7 588 646 | `7.59 M` | `7.6 M` | `7.59 M` |
| 825 031 | `825 k` | `825 K` | `825 k` |
| 24 832 751 | `24.8 M` | `25 M` | `24.8 M` |

`tests/Canger.Core.Tests/Model/HumanReadableSizeTests.cs`, 20 tests, including all six of
ranger's own doctests verbatim.

**Two settings found dead while in here**, neither fixed: `size_in_bytes` is defined, typed and
readable through `CangerSettings` and consumed by nothing — ranger uses it to print the raw
locale-formatted number instead of a prefix. And `binary_size_prefix` reaches only the linemode;
`StatusBar` and `BrowserColumn`'s own size call sites always format decimal, so turning it on
gives two different renderings of the same quantity on one screen.

### Visual mode escaped its directory, and `uv` could not end it

Reported: <kbd>V</kbd> then <kbd>3j</kbd> in a directory, and afterwards `uv` would not unmark,
the mode could not be left, and walking up marked files in the parent and its parent too — with
quitting the only way out.

**`uv` fought itself.** `uv` is `mark_files all=True val=False`. It cleared the marks, and then
`UpdateVisualSelection` — which runs after every command — put the visual range straight back.
Ranger ends the mode whenever the whole listing is acted on, precisely because the selection is
the thing being replaced (`core/actions.py:761-763`). `mark_files all=True` now does the same, so
both `uv` and `v` end a selection as they do in ranger.

**The mode was not bounded to anything.** The anchor is a row *number*, so in any other listing it
points at an unrelated file: pressing `h` carried the mode into the parent, where the next
movement marked a fresh range from that number. Ranger avoids this by calling
`change_mode('normal')` from nine separate places — moving left, `move_parent`, `enter_dir`,
`traverse` both ways, two tab paths, `reset`, `open_console`. Canger had it in three.

Rather than chase the other six, visual mode now remembers the directory *node* and tab number it
began in, and ends the moment either changes. That covers every route at once, including ones
nobody has thought of yet — bookmarks, `gg`, a plugin's `cd`. `open_console` is kept as an
explicit case, matching ranger: typing a command is not extending a selection.

The marks left behind in directories the user never selected anything in are also the likeliest
cause of the reported sluggishness, since every frame re-reads the whole marked set. A hard hang
was not reproduced. Extending a selection in the largest directory on this machine — 20,546
entries — measures 3.1 ms median, 18.4 ms worst.

Verified in a pty, the reported sequence exactly:

| keys | before | after |
|---|---|---|
| `V` `3j` | `4 marked` | `1.2 M/4  Mrk` |
| `uv` | still `4 marked` | unmarked, mode ended |
| `j` | `5 marked` | nothing marked |
| `h` | `3 marked` **in the parent** | nothing marked |
| `h` again | `16 marked` **in the grandparent** | nothing marked |

And the marks are not thrown away by leaving: `V` `3j` `h` `l` comes back to the same four, with
`j` no longer extending — which is what ranger does, since `change_mode` unmarks nothing.

### Marked folders read "16 B", and `dc`'s total was on the wrong side

`marked.Sum(e => e.Size ?? 0)`. A directory has no byte size of its own, so
`DirectoryNode.Size` reports its *entry count* — and summing that as bytes is where "16 B marked"
for two folders came from. Ranger skips a directory until `:get_cumulative_size` has measured it
(`gui/widgets/statusbar.py:288-289`), which is the only point at which there is an answer to give.

The right-hand side now follows ranger exactly:

- marked: `{size}/{count}` and `Mrk` **in place of** the position indicator — marks are easy to
  scroll away from and forget, and ranger gives up the position to say so (`statusbar.py:304-307`);
- all marked: the directory's own total, ranger's shortcut at `statusbar.py:285-286`, so the
  number does not appear to change the moment you press `v`;
- nothing marked: `{disk_usage} sum, {free} free`. The `sum` half was missing entirely — new
  `DirectoryNode.DiskUsage`, summed during the scan over files only, as ranger does
  (`container/directory.py:443`).

`dc` no longer announces a total on the message line. Ranger says nothing there; the total belongs
beside the count on the right, where it stays visible instead of appearing once and going away.

| | before | after |
|---|---|---|
| two folders marked | `2 B in 2 marked` | `0 B/2  Mrk` |
| then `dc` | left-hand message, right still `2 B` | `85 M/2  Mrk` |
| nothing marked | `414 G free  1/10  Top` | `11 M sum, 414 G free  1/10  Top` |

Sizes keep Canger's spacing (`11 M`, not ranger's `11M`), which is what the left of the same bar
already uses.

`tests/Canger.Core.Tests/Commands/VisualModeTests.cs` (5) and six in `StatusBarTests`.

### Extracting an archive runs on the task queue, not in front of the interface

`eae` on a large archive blanked the screen and held it: the plugin ran the archiver with the
`w` flag, which hands the terminal over and waits for a keypress afterwards. Nothing else could
be done meanwhile and there was no sign anything was happening.

Ranger has never done this. Its archive plugin builds a `CommandLoader` and puts it on
`fm.loader` (`plugins/ranger-archives/extract.py`), which is a `Loadable` like any other: it
shows in the task view with its description, spins the title bar, can be paused or cancelled, and
reloads the directory through an `after` signal when it ends. Canger had no equivalent at all —
`IProcessRunner` offered exactly two ways to run something, and both stop the world.

**What went into Canger** (the facility, usable by any command or plugin):

- `IBackgroundProcess` and `IProcessRunner.StartInBackground` — a program with both output streams
  piped and empty stdin, poll-shaped rather than `async` because its consumer is a step-at-a-time
  `ILoadable`. `BackgroundProcess` drains both streams continuously on the framework's threads:
  a pipe holds about 64 KB, and a command that talks too much would otherwise block writing and
  hang forever with nothing to show why.
- `CommandTask : ILoadable` — ranger's `CommandLoader`. One `WaitForExit(30ms)` per step, then
  yield, which is ranger's `select(..., 0.03)` written the other way up; without the wait the
  queue's 30 ms slice would busy-spin a core. Reports stderr when the program ends (a wrong
  password says so there and nowhere else), reports a non-zero exit that said nothing, and kills
  the program on `Dispose` so cancelling in the task view actually stops the work.
- `IFileManager.RunInBackground(description, command, workingDirectory, finished)` — the
  counterpart to `RunProgram`, and the equivalent of `fm.loader.add(CommandLoader(...))`.
- `IFileManager.ReloadDirectory(path)` — re-reads *one* directory rather than whichever is
  current. Background work started in one place may finish after the user has walked somewhere
  else, and it is the directory the files landed in that changed. Goes through the cache, so it
  is the node every tab and column is holding; then requests a redraw, since a finished task is
  not a keystroke.
- `TaskQueue.Throbber` and the title-bar spinner — ranger's `/-\|`, `#` while paused, turning
  once per slice (`core/loader.py:333-351`). Drawn at the first cell of the right-hand group,
  which is always a leading space, so it costs no width — ranger's `wid - right_sumsize`
  (`titlebar.py:44-46`). With one tab and no branch there is no group to borrow from, so it takes
  the last column; ranger never reaches that case because its bar always carries a key buffer.

**What went into the plugin** (`~/.config/canger/plugins/archives.cs`, and the `artifacts/` copy
the tests compile): `Runner.Run(..., ProcessFlags("w"))` became `RunInBackground`, with the
working directory captured up front and the completion callback reloading it by path — the
original's `refresh` closure, which captures `cwd` for the same reason. Still one task per
archive, so a single bad archive does not take the others down with it.

Verified in a pty at 110x24 on a 182 MB bzip2 tarball that takes about ten seconds:

| | |
|---|---|
| `eae` | console opens on `:extract` |
| Enter | status `Extracting 1 archive(s)`, browser still usable |
| +0.8s | title bar right edge shows `\|`; `src/` already appearing in the listing |
| +2.3s | spinner now `/` — turning, not stuck |
| `w` | task view shows `Extracting: slow.tar.bz2` |
| on finish | spinner gone, status back to the file line |

**Then the interface still felt sluggish, and it was.** Measured key latency in a pty: **1.1 ms
idle against 10.4 ms median and 40 ms at the ninetieth percentile** while an archive unpacked.
The queue is pumped from the same loop that reads the keyboard, so `CommandTask`'s
`WaitForExit(30ms)` was blocking the whole interface — a keystroke queued behind whatever was
left of the wait. Ranger has the identical flaw for the identical reason: its `select` watches
the child's pipes and not stdin (`core/loader.py:239`), so a keypress there waits out the
timeout too. Parity is not a reason to keep it.

The fix moves the waiting to the one place that is already watching the keyboard.
`ILoadable.Idle` — a default interface member, so every existing job keeps its behaviour
untouched — says how long there is no point asking for another step. `CommandTask` reports the
poll interval while its program runs and polls instead of blocking; the main loop spends that
time in `WaitForInput`, which a keypress ends at once.

Getting only that half made it **worse**: a flat 32 ms, and a core burned. With the step no
longer blocking, `TaskQueue.Work` spun its entire 30 ms slice asking `HasExited` thousands of
times. The slice has to end when the job reports itself idle. Both halves are needed, and both
are pinned by tests — reverting either one fails
`Work_TakesOneStepFromAnIdleJobRatherThanSpinningTheSlice`.

| | median | p90 | max |
|---|---|---|---|
| idle | 1.1 ms | 1.6 ms | 5.9 ms |
| during extract, before | 10.4 ms | 40.4 ms | 40.9 ms |
| during extract, after | **0.5 ms** | **0.8 ms** | **1.4 ms** |

Faster than idle, because the loop is already cycling rather than sitting in the two-second idle
poll. Canger's own CPU while unpacking: 3%, against 4% idle — so no spin was traded for it.

One more trap worth recording: `Process.HasExited` and the timed `WaitForExit` overload both
return before the async output handlers have necessarily delivered their last lines. Only the
untimed `WaitForExit()` joins them. `BackgroundProcess` therefore drains once on first seeing the
program exit — without it the tail of an error message goes missing, which is the half that
usually says what went wrong.

`tests/Canger.Core.Tests/Tasks/CommandTaskTests.cs` (12), `ThrobberTests.cs` (4),
`IdleTaskTests.cs` (7), three in `TabHeadingRenderTests` for the spinner's placement, and three
in `UserCommandsTests` that compile the real plugin and assert it queues rather than launches.
Those three were checked against a reverted plugin and all three fail on it, so they are pinning
the behaviour rather than describing it.

### Two compatibility tests had been silently skipping

`SampleRangerConfigLoadsEveryBinding` and `SampleRangerConfigSetLinesAllParse` both begin with
`Assert.SkipWhen(!File.Exists(TestPaths.SampleRangerConfig))`, and `TestPaths` looked for
`ranger_settings/rc.conf` while the directory in the repository is `ranger-settings`. One
character, and the whole "the user's real configuration must parse unchanged" guarantee was off
with nothing failing to say so — the same silence as a setting nothing consumes.

`TestPaths.SampleRangerConfig` now accepts either spelling. With the tests live again, one
assertion turned out to be stale rather than wrong-in-the-parser: it expected
`shell cp -rv --reflink=auto %c %d` for `pb`, and the configuration has since gained
`--preserve=timestamps`. Expectation corrected; 1259 tests, 0 skipped.

Worth remembering: a skip predicated on a path is a test that deletes itself when the path moves.

### Not bugs

Two of the four things reported were configuration, worth recording so they are not re-investigated:

- **No borders**, and **no image previews**. Canger's shipped `cc.conf` mirrors ranger's *default*
  `rc.conf` — `draw_borders none`, `preview_images false` — which is the right thing for a shipped
  default but is not the same as anyone's personal setup. Verified that `draw_borders both` draws
  and that the ueberzug helper starts and stays running with `preview_images true`.
- **Preview cache location** is already `~/.cache/canger`, alongside ranger's `~/.cache/ranger`.

Verified end to end: their real `rc.conf`, dropped in as `cc.conf` unchanged, loads 440 browser
bindings and renders with borders, counts, line numbers and a tilde-abbreviated title.

---

## PDF previews never appeared

Reported as "pdf preview cannot be seen". Images worked everywhere else, which is what made it
findable: the fault was not in the image pipeline but in the *name* Canger asked the script to
write to.

`ScopeScriptRunner.CachePathFor` returned a bare SHA-256 hex digest with no extension. Ranger's
equivalent is `'{0}.jpg'.format(sha512(...).hexdigest())` (`actions.py:1048-1054`) — the
extension is load-bearing, not decoration. Scope scripts come in two spellings:

- **write straight to `"${IMAGE_CACHE_PATH}"`** — 14 of the rules in the shipped script. These
  worked with either name, which is why every other image preview was fine.
- **strip the extension and hand the stem to a tool that appends its own** —
  `pdftoppm ... "${IMAGE_CACHE_PATH%.*}"` with `-singlefile -jpeg`. Given `<hash>.jpg` this
  round-trips exactly back; given a bare `<hash>` there is no dot to strip, so pdftoppm wrote
  `<hash>.jpg` while Canger went on to look at `<hash>`, found nothing, and returned
  `PreviewResult.None`. Silent — no error anywhere, because from the runner's point of view the
  script merely declined to produce an image.

PDF was the only rule genuinely broken by this. The font rule also uses `%.*`, but only to build
a `/tmp` scratch name, so it was unaffected.

Fix: `CachePathFor` appends `.jpg`, matching ranger exactly, which makes both spellings land on
the path the runner checks. Two regression tests, one per spelling — the strip-and-restore one
fails without the fix and the direct-write one guards against fixing PDFs by breaking the other 14.
Verified end to end against the real `~/.config/canger/scope.sh` and `~/.cache/canger`: exit 6,
and a JPEG at exactly the path Canger asks for.

Worth remembering as a shape: **a convention borrowed from another program can have a load-bearing
detail that looks cosmetic.** The hash was reimplemented thoughtfully — different algorithm, full
path, a comment explaining collision-avoidance — and the one part that was pure formatting turned
out to be the part the scripts depended on.

## The marked-size figure was highlighted along with the indicator

Reported from a screenshot: with files marked, the whole right-hand side of the status bar became
one solid block — `27.5 M/2  Mrk  VIS` — where ranger colours only the indicator.

`DrawRight` assembled its pieces with `string.Join` and wrote the result in a single style, built
as `InStatusbar + Scroll` plus `Marked` when anything was marked. The colour scheme gives
`in_statusbar + marked` `Bold | Reverse` with bright yellow — a deliberate block, because it has
one short word to draw attention to — and applying it to the joined line painted the byte count
and the free-space figure with it.

Ranger builds the same line as separately-tagged fragments (`gui/widgets/statusbar.py:253-327`),
each added with its own contexts and recoloured between by `_print_result`. What is worth noticing
is which fragments get *nothing*: the sizes. `right.add(human_readable(sumsize, separator=''))`
takes no context at all, and neither does `... + " sum"` or the free-space figure. Only the
indicators are flagged — `right.add('Mrk', base, 'marked')`, `'All'`/`'Top'`/`'Bot'`/percentage
with `base` plus their own key, `FROZEN` with `base, 'frozen'`. The numbers stay quiet so the flag
beside them can be loud.

`DrawRight` now carries a list of `(text, context)` and writes each piece with its own resolved
style, separators included — those were part of the block too, which is why it read as continuous.
`ScrollIndicator` returns its context alongside its word, so `All`/`Top`/`Bot`/`%` are tagged
individually as ranger tags them; the default scheme gives those four no colour, but a scheme is
free to, and that is the point of tagging them.

`VIS`/`UNVIS` has no ranger counterpart — ranger shows the mode on the left, in place of the
permission string — so it borrows `marked`, being the same kind of statement about the same set of
files. Verified in a pty: `Mrk` and `VIS` carry SGR `1;7;93`, the separator between them carries
none, and `50 k/2` beside them is unstyled.

The general shape, again: **a style that is correct for one word is wrong for the line it sits
in.** Joining first and colouring once is the convenient order and it silently widens every
highlight to the whole row.

## A visual selection absorbed files created inside it

Reported: with `A` and `C` selected by `Shift+V` and a command then writing `B` between them, `B`
joined the selection. Space-marking the same two files did not have the problem.

That difference is the whole diagnosis. Space marking sets `IsMarked` on a file and never revisits
it, and a reload carries marks across by path (`DirectoryNode.RestoreMarks`), so the two survive
untouched. Visual mode instead *re-derived* the range: `UpdateVisualSelection` ran after every
command and marked everything currently lying between the anchor row and the cursor row. A file
written into that gap was between them by the time the next command ran, so it got swept in.

Ranger cannot do this, and the reason is placement rather than logic. Its sweep lives inside
`move` itself (`core/actions.py:522-559`) — nothing else calls it. A listing that gains a file
while the cursor sits still is never re-swept. Canger had lifted the sweep out to a single place
after command dispatch, which reads as tidier and is how the behaviour was lost: "after any
command" is a much larger set of moments than "when the user moved".

An earlier comment on `StatusBar.IsVisualMode` asserted that absorbing new files *was* ranger's
behaviour and therefore correct. It was wrong, and it is corrected in place — a wrong citation is
worse than none, because the next reader stops looking.

Two fixes, one cause — a row number is not a stable name for a file when the listing can change
underneath:

- The sweep now runs only when the cursor is on a different file than before the command. Compared
  by path, not by reference: a reload rebuilds the entries, so reference equality would read every
  reload as a movement and defeat the guard.
- The anchor is remembered as a path and its row found at sweep time. Otherwise a file arriving
  *above* the anchor shifts every row below it and the next real movement sweeps a range the user
  never chose. Ranger keeps only the number and clamps it (`core/actions.py:525`); that clamp is
  kept as the fallback for when the anchor's own file has been deleted.

The arithmetic moved to `VisualRange` so it can be tested without a terminal — six tests, plus a
pty run of the reported sequence. Measured both ways: without the fix the status bar goes from
`8 B/2` to `12 B/3` when `b.txt` appears; with it, it stays at `8 B/2`.

## Cut and copied files were not dimmed

Reported: in ranger, `dd` and `yy` grey out what is on the clipboard, the marking survives leaving
the directory and coming back, and `uy` restores the colour. Canger showed nothing at all.

The whole clipboard worked — `CopyBuffer`, `IsCutPending`, `SetCopyBuffer`, `uncut` on `ud`/`uy`,
paste. Both stock colour schemes had carried the rule from the start:
`HasAny(Cut, Copied) && !Has(Selected)` → bold on bright black, which is ranger's
`colorschemes/default.py:70-77` line for line. Nothing anywhere produced the two keys.
`BrowserColumn.ContextFor` set fourteen of them and not these, so every row resolved as though the
clipboard were empty.

This is the same shape as the dead-settings audit and the tag marker before it: the mechanism, the
storage and the styling all present and correct, with no line joining the last two. Worth
remembering that a colour scheme handling a key proves nothing about whether the key is ever set —
it reads like evidence and is not.

Now `ContextFor` sets `Cut` or `Copied` from a set of paths handed down through the views the same
way `Tags` is. By path rather than by node, because the buffer outlives the listing it was filled
from — returning to a directory rebuilds every entry, and ranger recomputes
`[f.path for f in self.fm.copy_buffer]` on each draw for the same reason
(`gui/widgets/browsercolumn.py:294`). Kept as a `HashSet` maintained in `SetCopyBuffer` instead of
rebuilt per frame, which ranger can afford and a redraw loop should not pay for.

Ranger's `not context.selected` is honoured: the cursor row is already reverse video and dimming it
too would make it unreadable.

`jungle` inherits from the default scheme and so gets this; `snow` does not define the rule — and
neither does ranger's own `snow.py`, so that is a match rather than a gap.

Five tests, four of which fail without the two lines. Verified in a pty: `a.txt` renders SGR
`0;1;90` after `dd`, still `0;1;90` after leaving the directory and returning, and bare after `uy`.

## The help dumps could not be searched

Reported: `?` opens the key bindings, but `/` does nothing there, unlike ranger.

Canger sent all three dumps to its own pager, which scrolls and nothing else. Ranger's pager is
just as bare — there is no `/` in its `pmap` block either — but ranger never shows the dumps in it.
`dump_keybindings` writes a temporary file and calls `_run_pager`, which is one line
(`core/actions.py:1483-1484`):

    self.run(shlex.split(os.environ.get('PAGER', ranger.DEFAULT_PAGER)) + [path])

The search comes from `less`, not from ranger. Which is the interesting part: the feature was never
implemented by either program, and reimplementing it would have been the wrong answer to the
report — the right one was to notice that ranger delegates, and delegate the same way. Whoever
reads this next: check whether the upstream feature is *implemented* or *delegated* before building
it.

`ShowInExternalPager` now writes the text to a temporary file and runs `$PAGER` on it, defaulting
to `less` as ranger does. Previews and command output stay in the built-in pager, exactly as
upstream. Two divergences, both deliberate: the file is created with `FileMode.CreateNew` and
`0600` before anything is written to it, and a missing `$PAGER` falls back to the built-in pager
rather than showing nothing.

Verified in a pty: `?` then `k` puts `less` on a `canger-*` temporary file, and
`/--- taskview ---` scrolls to that section. Four tests, two of which fail without the routing.

## `:shell` would not complete a filename

Reported: `:shell some-program FIL<Tab>` did nothing. Completing the *program* name had been added
earlier in this same session, which is what makes this worth writing down — the fix at the time
read ranger's `shell.tab` far enough to find `get_executables()` and stopped there. The method has
three branches (`config/commands.py:320-342`), keyed on where the cursor is:

| where | ranger offers |
|---|---|
| no space yet | the programs on `$PATH` |
| just after a space | the selection — one file by name, several as `%s` |
| part-way through a word | the files here whose names begin with it |

Only the first was implemented; the other two returned an empty list. A command line was the one
place left in Canger where a filename had to be typed out in full — and the earlier fix had made
that *less* obvious, because Tab now visibly worked on the first word.

Two divergences from ranger, both forced by Canger quoting differently:

- **Matching is against the plain name, not the escaped one.** Ranger compares
  `file.shell_escaped_basename.startswith(start_of_word)`, which works there because its
  `shell_escape` leaves an ordinary name untouched. `MacroExpander.ShellQuote` wraps
  unconditionally, so `'FILE.txt'` would stop matching `FIL` the moment anything needed quoting.
- **Names are quoted only when they need it.** Not cosmetic: the completed word goes back into the
  console, and a name that returned wrapped in quotes would no longer match itself if the user
  carried on typing, so cycling would lose it.

Quoting goes through `QuoteForCommandLine`, not `ShellQuote` — the completed line is expanded again
when it runs, so `x%sy.txt` would otherwise carry a macro into a command line. That is the same
trap `ShellWord.Quote` fell into in the personal config.

The old `OffersNothingOnceTheProgramIsNamed` test asserted the missing behaviour and passed only
because the fixture directory was empty. Worth remembering: a test that pins an absence proves
nothing when the fixture cannot produce a presence.

Verified in a pty: `FIL<Tab>` gives `FILE_ONE.txt`, again gives `FILE_TWO.txt`, and `two<Tab>`
gives `'two words.txt'`.

## Permission changes did not show until `Ctrl+R`

The report was "I change permissions and do not see them until reset". The first three things I
tried all worked — `:shell chmod` on a file in the current directory, on a directory in the current
listing, and creating a file in the previewed one — which nearly had me answer "works here".

What actually reproduces it is the workflow the user's tool implies. `directory-file-permission-set-tui`
takes a *folder* and recurses into it, so it is run from the parent with the cursor on the folder,
and the folder is then entered to check. That sequence:

| | before | after |
|---|---|---|
| inside `sub` | `-rw-------` | `-rw-------` |
| back to the parent, chmod the file, enter `sub` again | `-rw-------` (stale) | `-rwxrwxrwx` |

Two things had to line up. `sub` was already loaded, so entering it reuses the interned node rather
than scanning; and `chmod` changes a file's **ctime**, not the containing directory's **mtime**,
which is the only thing staleness is judged on (`DirectoryNode.Load`, and ranger's
`container/directory.py:700` — the same test, so ranger has the same blind spot). Nothing between
those two points ever marks the listing out of date, and `Ctrl+R` only worked because `reset`
throws the whole cache away.

`RunProgram` reloaded only the current directory. It now reloads every directory on screen — the
pathway, the preview column, and in multipane every tab's directory. The finished-background-work
path in the main loop uses it too, which is the same event and covers a paste landing in the column
to the right.

Deliberately *visible* rather than *loaded*. `DirectoryCache.Trim` exists and has no callers, so
the cache holds every directory of the session; re-scanning all of them synchronously would stall
the interface at around 5 ms per two thousand entries, and would reach paths on media since
unplugged or on a mount that has stopped answering, with no key to press to escape it. What is on
screen is a handful of columns, and they are by definition the ones being looked at.

`Browser.VisibleDirectories` is static and takes what it needs, so the rule is unit-testable
without a terminal — six tests. It is the same set `ApplySettingsToDirectory` already walked, which
is not a coincidence: both answer "what is the user looking at".

Still not fixed, and deliberately: a `chmod` from another terminal. No directory-mtime poll can see
it, so that needs re-statting visible rows on a timer — a real divergence from ranger, not taken on
without asking.

`DirectoryCache.Trim` having no callers is left as a separate concern: memory growth over a long
session, not correctness.

## The directory cache grew for the whole session

`DirectoryCache.Trim` existed and had no caller. It turned out to be a faithful port of ranger,
where `garbage_collect` exists and the periodic call is commented out in the main loop
(`core/fm.py:531-534`); the only live caller there is `reset`, which is Canger's `Ctrl+R` →
`Directories.Clear()`. The dead constants came across too — `TIME_BEFORE_FILE_BECOMES_GARBAGE`
is 1200 seconds and nothing reads it.

Measured before deciding anything: about a kilobyte per cached entry, 88 MB → 143 MB over 100
directories of 400 files. Then the measurement that decided the shape of the fix — pressing
`Ctrl+R`, which drops every cached directory, moved RSS from 143 MB to 154.6 MB and left it there.
**Eviction cannot return memory to the system**; .NET does not hand it back. So this was never
about RSS going down, only about the heap not having to grow to hold listings nobody will look at
again — and it was worth saying so before building rather than after.

Which is why the unit is the *listing*, not the node. `Intern`'s promise is one object per path —
it is what makes a size measured in one column visible in another — and evicting a node something
still references would let the next lookup mint a second one, with its own cursor row, its own
marks and no measured size. Dropping `_allEntries` frees effectively all of the memory and cannot
break that: there is only ever one node. Nothing needed telling, either, because
`DirectoryCache.GetLoaded` and `DirectoryNode.LoadIfOutdated` both scan when `IsLoaded` is false,
so an unloaded directory refills the moment it is entered or drawn.

`Browser.RetainedDirectories` protects every tab's columns rather than only the visible tab's.
Unloading a background tab's directory would be safe, but it would put a scan in front of every tab
switch to save a handful of directories out of hundreds. Ranger draws the line in the same place.

One real trap, and the test for it was written before the code was: `Load` opens with
`RememberMarks()`, which reads the entries. After an `Unload` there are none, so it concluded
nothing was marked and wrote that over the marks `Unload` had just saved — a selection would have
vanished while the user was in another directory. `Load` now only remembers marks when it is
reloading over a listing that is actually there.

Measured after, against a control with the sweep disabled and the thresholds shortened so it fires:

| | first 100 directories | next 100 | final RSS |
|---|---|---|---|
| with the sweep | +38.0 MB | **+2.6 MB** | 131.7 MB |
| without | +58.7 MB | +44.1 MB | 193.9 MB |

The second hundred cost almost nothing because the first hundred's listings had been given back.
Without it the climb is linear and never stops.

Worth remembering: a method with no callers is a question, not a defect. The useful move was
asking what upstream does with its equivalent — the answer was "nothing, deliberately or by
neglect" — and then measuring, which said the obvious fix would not have worked.

## Copy-clash names counted from the second copy

Pasting `notes.md` into its own directory four times gave `notes_.md`, `notes_0.md`,
`notes_1.md`, `notes_2.md`.

That is ranger's rule (`ext/safe_path.py:13-21`): try a bare underscore, and only start counting
once that is taken. Canger already diverged from it in one respect — the suffix goes before the
extension, so a copy still opens in the right program — but kept the two-stage shape.

The report is right, and the reason it took four copies to notice is the interesting part: with
one clash the scheme looks fine, and every test covering it used exactly one clash. Over a run it
falls apart — three of the four are numbered, the odd one out is the oldest, and nothing in the set
says which came first. Both variants now count from zero, so the rule is one sentence and the
copies sort by the number that names them.

Left alone deliberately: a `notes_.md` from before this change is simply a different name.
Numbering starts at zero beside it rather than adopting it as the zeroth copy, which would mean
guessing at a file the user may have named themselves. There is a test for that.

Verified in a pty against the real config: `yy` then `pp` four times gives `notes_0.md` through
`notes_3.md`.

The lesson for the suite: **a test that exercises one iteration of a sequence cannot see a rule
that is wrong about sequences.** Five tests covered this naming and all five stopped at the first
collision.

## `./build.sh dist` — tarballs to hand to somebody else

Two, because there are two kinds of recipient: one who has .NET 10 and wants a small download, and
one who has nothing and would rather take 61 MB than install a runtime first.

| | tarball | needs |
|---|---|---|
| framework-dependent | 13 MB | .NET 10 runtime |
| self-contained | 61 MB | nothing |

The self-contained one was verified under `env -i` with no `dotnet` on `PATH`: it starts, and
Roslyn still compiles `commands.cs` — 46 commands out of the real personal config, with no SDK
anywhere. That was the part worth checking, since runtime plugin compilation is the one feature
that could plausibly have needed an SDK.

Both carry `config/`, and there is a check that they do. Without it Canger is not degraded, it is
inert: `--config` reports `key bindings: browser 0, console 0, pager 0, taskview 0`. The shipped
`cc.conf` *is* the keymap, and it is the one part of the payload that comes from content files
rather than a project reference, so it is the one that can quietly go missing.

Both also carry `LICENSE`, `README.md` and `doc/canger.1`. The licence is an obligation rather than
a courtesy — Canger is GPL-3.0-or-later, being a port of ranger, so a binary handed to anyone
obliges the corresponding source to be available to them.

### The trap this turned up

The first version built both variants through the same `obj/`, and **the framework-dependent
tarball aborted on startup with no message at all** — exit 134, nothing on stdout or stderr, and
`dotnet canger.dll` equally silent. It reproduces exactly: publish self-contained, then publish
framework-dependent, and the second one is broken. The ReadyToRun images left in the intermediates
by one configuration are compiled against a runtime the other does not have.

Three things make it nasty. It is silent. It depends on what happened to be built last, so it comes
and goes. And the artifact looks entirely normal — the earlier broken tarball was 3.8 MB against
13 MB, which reads like the trimming working rather than ReadyToRun having been lost.

So each variant now publishes through its own `--artifacts-path`, and every tarball is started
before it is packaged — `./canger --version`, which touches the host, the runtime and managed
startup, which is all that failure mode needs. A publish that emits a broken assembly still reports
success, so running the thing is the only check worth having.

Worth remembering: **a size that drops more than expected is a symptom, not a win.** The 3.8 MB was
the bug announcing itself and it read as good news.

### What `publish` leaves in that `dist` does not

`-p:SatelliteResourceLanguages=en -p:DebugType=none -p:GenerateDocumentationFile=false` — thirteen
Roslyn translation directories, the symbols and the API documentation, about 8 MB of a 37 MB tree.
Nothing reads any of it at runtime.

## `r` showed no programs to open with

`r` is bound to `chain draw_possible_programs; console open_with%space`, and the first half of that
chain was writing its answer to the **status bar** — where the second half then opened the console,
in the same place, and covered it. A dozen programs were truncated to a line and the line was
hidden a moment later by the next link in its own chain.

Ranger does not use the status bar for this. `draw_possible_programs` sets `ui.browser.draw_info`
(`core/actions.py:947-960`) and the view draws those lines over the bottom of the listing
(`gui/widgets/view_base.py:97-107`), in the same slot as the bookmark and hint windows and with the
same precedence — `if draw_bookmarks ... elif draw_hints ... elif draw_info`. It stays up while the
console is typed into, which is the entire point, and `Console.close` clears it
(`gui/widgets/console.py:179`).

Canger now has `IFileManager.ShowInfo`, an overlay drawn bottom-anchored and no taller than it
needs, and taken down when the console goes.

**Getting that second part right took two attempts, and the first one is the lesson.** It cleared
the overlay from a helper that the console-closing paths were routed through — "one place, so the
overlay cannot outlive the console by way of a route that forgot about it", as the comment
confidently said. It forgot a route. `console_accept` calls `ConsoleWidget.Accept`, which closes
the widget itself and never goes near the browser's closing path, so *running* a command from the
console — the ordinary way to use this feature — left the list of programs on screen with nothing
to dismiss it. Reported as "it seemed stuck on the screen", and it was.

There are two `Accept` calls and three `Close` calls. The fix is not a fourth call site: it is to
stop enumerating them. `HandleConsoleKey` is the single funnel for every key the console sees, so
it now asks afterwards whether the console is still open and drops the overlay if it is not. That
covers all five paths and any added later.

Worth remembering: **"I routed them all through one place" is a claim about a set you have to have
enumerated correctly.** Asking about the resulting state needs no such enumeration. The first
version even said the safe-sounding thing in a comment, which is how it read as done.

Two details taken from ranger rather than invented. The number is right-justified to the widest
(`core/actions.py:955`), so a list running into double figures reads as a column. And each line
carries the **command**, not the label: ranger lists `program[1]`, and two rules for the same
program differ only by their command — the shipped `rifle.conf` has four `editor:` rules.

Verified in a pty against the real config, with a control build: `r` lists ten programs over the
listing with the console below, the list survives typing into it, and both accepting a choice and
pressing escape take it down. Without the fix the count after accepting stays at ten.

No unit test: the invariant lives in `Browser`, which needs a terminal to construct, and the four
tests on `draw_possible_programs` itself cover only what the command produces. The pty run against
a control is the evidence here.

The shape worth remembering: **a one-line channel cannot answer a question the user needs while
they type.** The status bar was the wrong instrument, not a badly used one — and the chain was
built so that the answer's own successor destroyed it.

## The size ran into the end of a truncated name

Reported from a screenshot: `hi-trevor-my-read-after-the-call_2026-08-26_12-17~1.9 k`, with the
size against the ellipsis and no space between them.

`BrowserColumn` did reserve the gap — `detailWidth` is the size's width **plus one** — and then
spent it in the wrong place. The size was written at `Bounds.Right - detailWidth`, one column left
of the edge, so the spare column landed *after* the size, against the border, where nothing needed
it. The name then filled every column up to the size. One character in the wrong expression, and
the arithmetic that was supposed to produce the gap produced a trailing blank instead.

Ranger avoids the question by carrying the space inside the string it lays out:
`infostring.append([" " + infostringdata, ...])` (`browsercolumn.py:410-411`). The gap is part of
the thing, so it cannot be reserved in one place and drawn in another.

The size is now written at `Bounds.Right - detailText`, flush to the edge, and the reserved column
falls where it was meant to. Four tests: the truncated case, the size ending at the last column,
the untruncated case where the gap is padding rather than the reserved column, and the existing
guard that drops the size entirely rather than crush the name.

None of the 1528 tests noticed, because the row's *contents* were all anyone had ever asserted —
`StartsWith(" alpha.txt")` and the like. Where things sit was untested, so a layout bug had nowhere
to fail. The new ones read specific cells.

## The preview column flickered while a PDF preview was generated

Reported as a twitch down the right of the screen while a PDF preview is produced, gone once it is
cached.

Previews are generated on a worker so the browser does not stop dead on every video thumbnail, and
`Preview` returned `PreviewResult.None` for the frames in between. `None` is also what "there is
nothing to preview here" looks like, and with `collapse_preview` on that is the answer that
**removes the column**. So: land on a PDF, the column collapses because the answer has not arrived,
the answer arrives a fifth of a second later, the column comes back. Two frames of a different
layout, and the whole right-hand side shifts twice.

"Nothing to show" and "not ready yet" are indistinguishable to a drawing routine and opposites to a
layout one. `PreviewKind.Pending` now separates them, and `MillerView.CountsAsPreview` repeats the
previous frame's decision when the answer is pending rather than making a new one. Ranger reaches
the same place by a different route: `_collapse` consults the file's cache entry and returns
`old_collapse` when there is not one yet (`gui/widgets/view_miller.py:196-201`).

### Measuring it took three attempts, and the first two said "no bug"

Sampling the reconstructed screen every 100 ms found one layout with the fix and one without —
because the whole episode is about two frames inside a 200 ms window, and a poll that slow steps
over it. Counting border rows in the raw stream found none at all, because rendering is
differential and an unchanged border is never rewritten.

What worked was forcing the issue: send `<C-l>` forty times through the generation window so every
intermediate state is actually painted, then count distinct border rows in the output. With the
fix, forty identical. Without it, thirty-eight identical and **two collapsed** — the preview column
missing from both.

Worth remembering: **"I could not reproduce it" is a statement about the instrument** until the
instrument has been shown to be able to see the thing. Two measurements agreed the bug was absent
and both were too coarse to see a two-frame event. The fix was written before any of them, from
reading the code, and nearly got reverted as unvalidated.

## What is left

Nothing from ranger. Possible directions from here:

- **Sixel and iTerm2 image protocols.** The eight backends are implemented, but only kitty and
  ueberzug have been exercised against real terminals.
- **`--profile` and `--logfile`**, which ranger has and Canger does not. Neither affects
  behaviour; both are debugging aids.
- **A `canger.desktop` and a man page install path.** `./build.sh dist` produces the tarballs; what
  is missing is the desktop entry and somewhere for `doc/canger.1` to land so `?` → `m` works
  without the tarball's own directory.
- **Noticing a `chmod` made in another terminal.** Both Canger and ranger judge staleness by the
  directory's mtime, which a `chmod` does not touch, so neither sees it. Fixing it means
  re-statting the visible rows on a timer — bounded, but a real divergence from ranger, and not
  worth doing unasked.
- **Performance work on very large directories.** Nothing is known to be slow; nothing has been
  measured either.

### Watch these in daily use

Everything below landed on the same day and has had no living-with. They are not suspected of
being wrong — each is tested and was verified in a pty — but they are the least-exercised things
in the tree, and two of them touch something that matters:

- **`unload-idle-directories`** only fires after twenty minutes idle, so at its real threshold it
  has effectively never run. It was verified by shortening the interval to seconds, which is not
  the same as living with it. It drops listings silently; the sign of it going wrong would be a
  selection or a cursor row lost on returning to a directory left alone for a while.
- **`numbered-clash-suffixes`** changes what a pasted file is named. Well tested, but the kind of
  change where being wrong touches files rather than pixels.
- **`reload-visible-directories`** now re-reads every column after a command rather than one, and
  after background work finishes.

### Verifying by driving the real binary

Several defects in this file were found only by running the published binary under a pty and
reconstructing the screen from the escape stream — the unit tests could not see them. There is no
committed harness; the scripts were written per-investigation in a scratch directory and are gone.
What is worth knowing before writing the next one:

- The window size must be set explicitly with `TIOCSWINSZ`. Without it the pty reports 0x0 and
  nothing lays out.
- The status bar writes text in separately positioned chunks, so searching the raw stream for a
  multi-word phrase gives false negatives. Match single tokens.
- A naive replayer that does not implement scrolling will show the first screenful and pile
  everything after it onto the last row — which reads exactly like a program that has stopped
  responding. `less` searching correctly was misdiagnosed twice this way.
- Reading per-cell SGR state is what proves a colour question. `1;7;93` on one word and nothing on
  the next is the difference between a fix and a plausible-looking one.
