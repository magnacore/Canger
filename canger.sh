#!/bin/sh
# Launches Canger with the environment its commands need.
#
# A program started from a window-manager keybinding inherits a bare PATH: no ~/.local/bin, so
# none of the personal scripts a command runs can be found. This puts that right without going
# through a full shell startup — the xonsh launcher beside this one does the same job by sourcing
# ~/.xonshrc, which costs a Python interpreter every time Canger opens.
#
# Only the variables Canger itself reads are set here. Everything else in ~/.xonshrc is either
# xonsh's own business or only matters to an interactive prompt.
#
# Install it wherever the keybinding points, or run it directly:
#
#   ./canger.sh ~/UNIT_TESTS
#
# Set CANGER_BINARY to run a different build; CANGER_DOTNET_ROOT if the SDK moves.
set -eu

# realpath, so the build is found beside the real file even when this is reached via a symlink
# on the PATH.
self=$(readlink -f "$0")
here=$(dirname "$self")

# Prepended, and only when missing, so an environment that already has it is left alone and the
# entry cannot pile up on repeated launches.
case ":${PATH}:" in
    *":${HOME}/.local/bin:"*) ;;
    *) PATH="${HOME}/.local/bin:${PATH}" ;;
esac

# Where the SDK is. CANGER_DOTNET_ROOT wins; then the path it lives at on the machine Canger was
# written on, which is outside the default search path; then whatever `dotnet` is on PATH, which
# is how it is found on anyone else's machine. Without one of these a .NET apphost finds no
# runtime, and the failure it reports does not say so.
if [ -n "${CANGER_DOTNET_ROOT:-}" ]; then
    DOTNET_ROOT="$CANGER_DOTNET_ROOT"
elif [ -x /opt/anaconda3/envs/dotnet/lib/dotnet/dotnet ]; then
    DOTNET_ROOT=/opt/anaconda3/envs/dotnet/lib/dotnet
elif command -v dotnet >/dev/null 2>&1; then
    DOTNET_ROOT=$(dirname "$(readlink -f "$(command -v dotnet)")")
else
    echo "canger: no .NET SDK found. Install .NET 10, or set CANGER_DOTNET_ROOT to its directory." >&2
    exit 1
fi
export DOTNET_ROOT

# What :edit and the rifle editor rules look for.
VISUAL="${VISUAL:-/usr/bin/nvim}"
EDITOR="${EDITOR:-$VISUAL}"
TERMINFO="${TERMINFO:-/usr/share/terminfo}"

export PATH VISUAL EDITOR TERMINFO


# Prefer the fastest build that exists: the published ReadyToRun output, then Release, then the
# Debug build. Measuring Canger against a Debug build is how its startup came to be reported as
# three times what it needs to be, so the launcher now says which one it picked when it is not
# the fast one.
resolve_binary() {
    for candidate in \
        "${here}/src/Canger.App/bin/Release/net10.0/${CANGER_RID:-linux-x64}/publish/canger" \
        "${here}/src/Canger.App/bin/Release/net10.0/canger" \
        "${here}/src/Canger.App/bin/Debug/net10.0/canger"
    do
        if [ -x "$candidate" ]; then
            printf '%s' "$candidate"
            return 0
        fi
    done
    return 1
}

binary="${CANGER_BINARY:-$(resolve_binary || true)}"

if [ -z "$binary" ] || [ ! -x "$binary" ]; then
    echo "canger: not built yet" >&2
    echo "canger: run ${here}/build.sh publish" >&2
    exit 1
fi

case "$binary" in
    *"/bin/Debug/"*)
        echo "canger: running the Debug build; ./build.sh publish is markedly faster" >&2
        ;;
esac

# exec, so Canger owns the terminal directly and signals reach it rather than this shell.
exec "$binary" "$@"
