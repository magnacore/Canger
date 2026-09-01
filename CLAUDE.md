Ranger is a python based TUI file manager. Source code is given in ranger-master.

Port Ranger from python to modern .Net 10 C# in Canger folder.

## While porting, pay attention to:
- The port should be a 1-1 feature match
- Even though the features should match, this does not mean python idioms need to be imitated. Use modern C# features and best practices.
- No need to copy python file structure. Use modern C# and OOP practices.
- Canger should be extendable via plugins written in C#
- Just like there is commands.py, rc.conf, rifle.conf, scope.sh, there should be commands.cs, cc.conf, scope.sh and rifle.conf to set the configurations. Sample configurations are provided in ranger_settings
- Use exact same shortcuts as Ranger
- Use parallel processing wherever it makes sense
- Write unit tests so Canger can be tested using Test Driven Development
- Develop Canger one system at a time
- Setting will be saved in ~/.config/canger

Note:
- .NET is available in /opt/anaconda3/envs/dotnet/lib/dotnet/dotnet
- Write well documented code
- Write secure code
- Write maintainable code
- Write extendable code
- Write modular code
- Do not use var - use explicit types
- Development may be done in multiple sessions so maintain a TODO list so work can be resumed
- Do not modify anything outside the repo directory without permission
- Use gitflow methodology for branches

## Verifying a change

The port is complete; the work now is defect-driven. These apply to every fix, not on request.

- **End every fix with a control.** Put the defect back — one small mutation — and confirm which
  tests fail. If none fail, the tests pin nothing and the fix is unverified. This has repeatedly
  caught tests that looked fine: a settings fix where 1868 of 1868 passed with the bug fully
  restored, an ordering test that was vacuous because the list had one entry, and a change with
  two halves that each masked the other and needed two separate mutations.
- **Check the mutation compiled.** A mutation that does not build measures the old binary.
- **Test where the defect is, not where the code is.** If the defect is *which* value a caller
  passes, a test that passes the right value itself can never catch it. Move the choice inside
  the thing under test so there is no seam left to get wrong.
- **Fix a logical bug even when ranger has the same bug.** The 1-1 mandate governs behaviour and
  shortcuts, not the reproduction of ranger's mistakes. Record the divergence and why.
- **Suspect any mechanism nothing feeds.** The commonest defect shape here, nine times over: a
  setter, context key, filter or invalidation hook that exists, compiles, is tested, and has no
  caller. After adding anything that reads state, look for what writes it.

## Two skills carry the methods

Both are in `Canger/.claude/skills/`, and both exist because the method was re-derived from
scratch more than once.

- **`pty-verify`** — driving Canger in a real terminal and reading the screen back, for anything
  visible. The harness is committed at `Canger/tools/screen.py`; do not write another one. Its
  five traps are listed in the skill and in the script's own docstring.
- **`ranger-parity-check`** — settling "does Canger match ranger here?" by running ranger's own
  Python and Canger's C# over the same input and diffing, rather than reading the source (which
  misleads) or driving ranger's UI (which fails).

## Working agreements

- Nothing is pushed. `main` and `develop` are local; the user will create the remote later.
- Only `Canger/` is a git repository.
- `~/.config/canger` is the user's live configuration and may be edited directly.
- Run `./build.sh publish` after merging, or `canger` keeps running the previous binary.
- Never risk data. No forced or lazy unmounts, no overwrite without a check, and read a file
  before deleting or replacing it.
