#!/usr/bin/env bash
# Builds the Canger solution.
#
# The SDK is found on PATH, or at the path it lives at on the machine Canger was written on.
# Set CANGER_DOTNET_ROOT if yours is somewhere else again.
#
#   ./build.sh                 Debug build, for development and for `dotnet test`
#   ./build.sh Release         Release build
#   ./build.sh publish         Release + ReadyToRun, which is what canger.sh prefers to run
#
# `publish` is the one that matters for speed: ReadyToRun precompiles the IL ahead of time and
# only applies at publish, so a plain build — in either configuration — still pays to JIT itself
# on the way to the first frame. Everything Canger measured before this existed was a Debug build.
set -euo pipefail

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
cd "$(dirname "$0")"

target="${1:-Debug}"
shift || true

case "$target" in
    publish)
        # -r is required for ReadyToRun. Framework-dependent by default in current .NET, which is
        # what Canger wants: no runtime is bundled, so the SDK's own runtime is used.
        exec "$DOTNET_ROOT/dotnet" publish src/Canger.App/Canger.App.csproj \
            -c Release -r "${CANGER_RID:-linux-x64}" --self-contained false "$@"
        ;;
    Debug|Release)
        exec "$DOTNET_ROOT/dotnet" build Canger.slnx -c "$target" "$@"
        ;;
    *)
        # Anything else is passed straight through, so `./build.sh --no-restore` still works.
        exec "$DOTNET_ROOT/dotnet" build Canger.slnx "$target" "$@"
        ;;
esac
