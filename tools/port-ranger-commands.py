#!/usr/bin/env python3
"""Ports the thin shell-wrapper commands from a ranger commands.py into Canger's commands.cs.

Most custom ranger commands are a docstring and one `execute_console("shell <tool> %s")` call,
sometimes with an argument that has a default, sometimes run once per selected file. Those
translate mechanically. Anything that calls into the file manager for real — fzf integration, tab
juggling, selection logic — is listed for hand-porting instead of guessed at.

The rule this tool follows is that it would rather refuse than guess. An earlier version matched
the command string with a regex over the unparsed source, which silently truncated

    self.fm.execute_console(f'''shell -f gpg --detach-sign -u KEY "{f.relative_path}" ''')

at the inner quote and emitted a command that ran gpg with no filename at all. Six commands were
wrong that way and nothing said so, which is much worse than six left on the to-do list. So the
template is now read from the f-string's own parts, every interpolation has to be one this tool
understands, and anything else is skipped by name.

Usage: port-ranger-commands.py <commands.py> <commands.cs>
"""
import ast
import re
import sys

# What an f-string may interpolate, and what it becomes in the generated C#.
#
# `f.relative_path` marks a command that runs once per selected file: ranger loops over the
# selection and runs the tool separately for each, which is not the same as passing them all at
# once, because these tools take exactly one argument.
PER_FILE = "f.relative_path"


def summary(node):
    """One line, since it goes in an attribute and an XML comment."""
    doc = (ast.get_docstring(node) or "").replace("\n", " ")
    doc = re.sub(r"\s+", " ", doc).strip().lstrip(":").strip()

    # The docstring often just repeats the class name; drop that.
    if doc.lower().startswith(node.name.lower()):
        doc = doc[len(node.name):].strip()

    return (doc.replace('"', "'") or f"Runs the {node.name} tool.").rstrip(".") + "."


def template_of(call):
    """The command string of an execute_console call, as (text, [interpolations]).

    Returns None when the argument is not a plain string or f-string, so a computed command is
    never half-understood.
    """
    if not call.args:
        return None

    arg = call.args[0]

    if isinstance(arg, ast.Constant) and isinstance(arg.value, str):
        return arg.value, []

    if not isinstance(arg, ast.JoinedStr):
        return None

    text, holders = "", []

    for part in arg.values:
        if isinstance(part, ast.Constant) and isinstance(part.value, str):
            text += part.value
        elif isinstance(part, ast.FormattedValue):
            expression = ast.unparse(part.value)
            text += "{" + expression + "}"
            holders.append(expression)
        else:
            return None

    return text, holders


def console_calls(node):
    """Every execute_console call in a class body, with where it sits.

    Returns (call, in_loop, guarded) per call. `guarded` means the call is inside a conditional
    *within* a loop — a per-file test this tool cannot express, such as

        for f in files:
            if os.path.splitext(f.relative_path)[1] == ".sig":
                self.fm.execute_console(...)

    which only verifies .sig files. Emitting the loop without the test would run the tool on every
    selected file instead. A conditional outside a loop is left alone, because that shape is
    always the `if not cwd or not cf: notify; return` guard, which never fails in practice.
    """
    found = []

    class Walker(ast.NodeVisitor):
        def __init__(self):
            self.loops = 0
            self.ifs = 0

        def visit_For(self, loop):
            self.loops += 1
            self.generic_visit(loop)
            self.loops -= 1

        def visit_If(self, branch):
            self.ifs += 1
            self.generic_visit(branch)
            self.ifs -= 1

        def visit_Call(self, call):
            if (isinstance(call.func, ast.Attribute)
                    and call.func.attr == "execute_console"):
                found.append((call, self.loops > 0, self.loops > 0 and self.ifs > 0))
            self.generic_visit(call)

    Walker().visit(node)
    return found


def is_argument(node, holder):
    """Whether a holder is a command argument rather than something computed.

    `directories_number_highlight` interpolates a `target_dir` built from the highlighted file, not
    from anything the user typed. Treating that as an argument produced a command that ran the tool
    on an empty string. A holder only counts if it is assigned from `self.arg` or `self.rest`.
    """
    text = ast.unparse(node)

    return re.search(re.escape(holder) + r"\s*=\s*self\.(arg|rest)\(", text) is not None


def default_for(node, holder):
    """The literal a holder falls back to when no argument was given."""
    text = ast.unparse(node)
    match = re.search(r"else:\s*\n?\s*" + re.escape(holder) + r" = ([^\n]+)", text)

    return match.group(1).strip().strip("'\"") if match else None


def analyse(node):
    """Returns a description of a thin wrapper, or None when it needs hand-porting."""
    calls = console_calls(node)

    if len(calls) != 1:
        return None

    call, in_loop, guarded = calls[0]

    # A per-file conditional changes which files the tool runs on, and this tool cannot express
    # that. Better named on the to-do list than quietly widened.
    if guarded:
        return None

    parsed = template_of(call)

    if parsed is None:
        return None

    template, holders = parsed

    # Anything reaching into the file manager beyond these three is doing real work.
    touched = set(re.findall(r"self\.fm\.(\w+)\(", ast.unparse(node)))
    if touched - {"execute_console", "notify", "change_mode"}:
        return None

    per_file = [h for h in holders if h == PER_FILE]
    arguments = [h for h in holders if h != PER_FILE and h.isidentifier()]

    # Every interpolation must be one of the two understood kinds, or this is not mechanical.
    if len(per_file) + len(arguments) != len(holders) or len(arguments) > 1:
        return None

    if per_file and not in_loop:
        return None

    if in_loop and not per_file:
        return None

    holder = arguments[0] if arguments else None

    if holder and not is_argument(node, holder):
        return None

    return {
        "template": template,
        "holder": holder,
        "default": default_for(node, holder) if holder else None,
        "per_file": bool(per_file),
    }


def csharp(name, doc, spec):
    pascal = "".join(p.capitalize() for p in name.split("_"))
    template = spec["template"]
    holder = spec["holder"]
    usage = f" Usage: {name} [<{holder}>]" if holder else ""
    lines = []

    if holder:
        default = spec["default"]
        value = f'"{default}"' if default is not None else '""'
        lines += [f'        string {holder} = Rest(1).Trim() is {{ Length: > 0 }} given',
                  f'            ? given',
                  f'            : {value};',
                  '']
        template = template.replace("{" + holder + "}", "{" + holder + "}")

    if spec["per_file"]:
        # One run per file, because ranger loops: these tools take a single argument, and handing
        # them the whole selection at once would pass extra arguments they do not expect.
        #
        # The Python templates wrap the interpolation in literal quotes — `-u KEY "{f.relative_path}"`
        # — because that is how a Python f-string quotes for the shell. The value is quoted here
        # instead, properly, so those literal quotes have to come off or the shell would be handed
        # `"'name'"` and the generated C# string would not even compile.
        placeholder = "{" + PER_FILE + "}"
        for quote in ('"', "'"):
            template = template.replace(quote + placeholder + quote, placeholder)

        body = template.replace(placeholder, "{ShellWord.Quote(entry.Basename)}")
        lines += ['        foreach (FsNode entry in FileManager.Selection)',
                  '        {',
                  f'            FileManager.Execute($"{body}");',
                  '        }',
                  '',
                  '        FileManager.ChangeMode("normal");']
    elif holder:
        lines.append(f'        FileManager.Execute($"{template}");')
    else:
        lines.append(f'        FileManager.Execute("{template}");')

    body = "\n".join(lines)

    return f'''
/// <summary>{doc}</summary>
[Command("{name}", Summary = "{doc}{usage}")]
public sealed class {pascal}Command : CangerCommand
{{
    /// <inheritdoc />
    public override void Execute()
    {{
{body}
    }}
}}
'''


source, output = sys.argv[1], sys.argv[2]
tree = ast.parse(open(source, encoding="utf-8").read())

ported, skipped = [], []

for node in tree.body:
    if not isinstance(node, ast.ClassDef):
        continue
    spec = analyse(node)
    if spec is None:
        skipped.append(node.name)
        continue
    ported.append(csharp(node.name, summary(node), spec))

with open(output, "w", encoding="utf-8") as f:
    f.write(f"""// SPDX-License-Identifier: GPL-3.0-or-later
// Generated from a ranger commands.py by tools/port-ranger-commands.py. Do not edit by hand:
// hand-written commands belong in a file of their own beside this one, or they will be lost the
// next time this runs.
//
// Each of these was a Python class whose whole body was one shell command. Canger compiles this
// file at startup, so editing it and restarting is all that is needed — the same loop as ranger's
// commands.py, in the language the rest of Canger is written in.
//
// {len(ported)} commands generated. Left for hand-porting, because they do more than run a shell
// command: {", ".join(skipped)}.

/// <summary>Quotes one argument for the shell these commands run through.</summary>
internal static class ShellWord
{{
    /// <summary>Wraps a word in single quotes, escaping any it contains.</summary>
    internal static string Quote(string value) =>
        "'" + value.Replace("'", @"'\\''", System.StringComparison.Ordinal) + "'";
}}
{"".join(ported)}""")

print(f"generated {len(ported)}, left for hand-porting {len(skipped)}")
print("hand-port:", ", ".join(skipped))
