#!/usr/bin/env bash
# Builds Canger if needed and runs it.
#
# The .NET SDK on this machine lives outside the default search path, so DOTNET_ROOT must be
# exported or the apphost cannot find a runtime. Override CANGER_DOTNET_ROOT if yours differs.
#
#   ./run.sh                 open the current directory
#   ./run.sh ~/Projects      open a directory
#   ./run.sh --help          list the options
#   CANGER_NO_BUILD=1 ./run.sh   skip the build and run what is already there
set -euo pipefail

export DOTNET_ROOT="${CANGER_DOTNET_ROOT:-/opt/anaconda3/envs/dotnet/lib/dotnet}"
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
