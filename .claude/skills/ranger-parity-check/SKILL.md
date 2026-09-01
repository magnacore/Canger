---
name: ranger-parity-check
description: Settle "does Canger match ranger here?" by running ranger's own Python and Canger's C# over the same input and diffing the results, instead of reading the source or driving the UI. Use for any parity question about behaviour, statuses, completions or command semantics.
---

# Checking parity by running both, not reading both

Reading ranger's source misleads. Its Bazaar head-commit parser looks correct and never returns
anything, because `_run` strips the trailing newline its own regex requires. Driving ranger's UI
is worse: three separate attempts in a pty failed to reach the code under test, and each produced
a confident null result.

The method that works is to **call ranger's own class directly**, call Canger's, and diff.

## Ranger's side

Its objects want a live file manager. Bypass the constructor and set only what the method
touches — for the VCS backends that is two fields:

```python
import sys, json
sys.path.insert(0, "/home/manuj/Projects/Canger/ranger-master")
from ranger.ext.vcs.git import Git

g = Git.__new__(Git)          # no __init__: it wants a directory object with settings
g.repotype = "git"            # _run builds its command line from this
g.path = repository

print(json.dumps({
    "root": g.data_status_root(),
    "subpaths": dict(sorted(g.data_status_subpaths().items())),
    "remote": g.data_status_remote(),
    "branch": g.data_branch(),
}, indent=2, sort_keys=True))
```

For a command rather than a backend, give it a stand-in `fm` exposing only what the method reads:

```python
import ranger.config.commands as rc
cmd = rc.shell("shell sudo mv canger /us")
cmd.fm = FM()                 # .thisdir.files, .thistab — whatever tab() actually touches
print(list(cmd.tab(0) or []))
```

That settled a reported "ranger completes this and Canger does not" in one run: ranger returned
nothing, so there was no parity gap to fix.

## Canger's side

Write a temporary xunit test that dumps the same shape to a scratch file, run `./test.sh`, read
it, then **delete the test**. It is an instrument, not a regression.

```csharp
[Fact]
public void Dump() => File.WriteAllText("/tmp/.../canger-git.json",
    JsonSerializer.Serialize(result, Options));
```

Then diff the two JSON files rather than eyeballing them.

## What to build the comparison over

One input holding every case at once. For the VCS backends that meant a repository with staged,
changed, deleted, a *real* merge conflict, untracked, ignored, a rename, an ignored directory and
an empty directory — plus separate repositories for each remote state, a detached head and no
remote. The rename and the empty directory both caught real parsing questions.

## Gotchas that cost time

- **`RANGER_LEVEL` is set in this environment.** Unset it or ranger refuses to nest.
- **Mercurial: ranger hardcodes `chg`.** There is no `chg` *package*; the `mercurial` package
  ships `/usr/bin/chg`. Ranger's hg backend does run.
- **Bazaar: `bzr` is Breezy behind an alternatives symlink.** It fails when anaconda's `python3`
  shadows the system one, so run with `PATH=/usr/bin:/bin:$PATH`.
- **Normalise before diffing.** Ranger returns hg's short revision as an int and Canger as a
  string; that is not a difference.

## When the diff is not empty

A difference is not automatically a Canger bug. Of the four backends checked this way, three
matched exactly and the fourth differed only where *ranger* is broken. And where both agreed on
something wrong — ranger's `svn status` parser reading its trailing `Summary of conflicts:` block
as a status record — the rule is to fix Canger anyway. Parity never justifies keeping a defect.
