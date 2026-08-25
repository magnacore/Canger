#!/usr/bin/env bash
# Builds Canger if needed and runs it.
#
# The SDK is found on PATH, or at the path it lives at on the machine Canger was written on.
# Set CANGER_DOTNET_ROOT if yours is somewhere else again.
#
#   ./run.sh                 open the current directory
#   ./run.sh ~/Projects      open a directory
#   ./run.sh --help          list the options
#   CANGER_NO_BUILD=1 ./run.sh   skip the build and run what is already there
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
here="$(cd "$(dirname "$0")" && pwd)"
# Deliberately the Debug build: this is the development loop, where a fast rebuild matters more
# than a fast start. Do not measure with it — use ./build.sh publish and canger.sh for that.
binary="$here/src/Canger.App/bin/Debug/net10.0/canger"

# Built quietly, so a rebuild does not scroll the terminal before the interface takes it over.
if [[ -z "${CANGER_NO_BUILD:-}" || ! -x "$binary" ]]; then
    "$DOTNET_ROOT/dotnet" build "$here/src/Canger.App" -v quiet --nologo >/dev/null
fi

# The working directory is left as the caller's, so a bare `./run.sh` opens where they are.
exec "$binary" "$@"
