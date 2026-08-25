#!/usr/bin/env bash
# Builds the Canger solution.
#
# The .NET SDK on this machine lives outside the default search path, so DOTNET_ROOT must be
# exported for the test apphosts to find a runtime. Override CANGER_DOTNET_ROOT if yours differs.
#
#   ./build.sh                 Debug build, for development and for `dotnet test`
#   ./build.sh Release         Release build
#   ./build.sh publish         Release + ReadyToRun, which is what canger.sh prefers to run
#
# `publish` is the one that matters for speed: ReadyToRun precompiles the IL ahead of time and
# only applies at publish, so a plain build — in either configuration — still pays to JIT itself
# on the way to the first frame. Everything Canger measured before this existed was a Debug build.
set -euo pipefail

export DOTNET_ROOT="${CANGER_DOTNET_ROOT:-/opt/anaconda3/envs/dotnet/lib/dotnet}"
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
