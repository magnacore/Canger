#!/usr/bin/env bash
# Runs the Canger test suite.
#
# Two things to know before changing this:
#   1. DOTNET_ROOT must be exported. The SDK is not in a default location, and without it the
#      test apphosts fail to launch and the failure surfaces only as "Zero tests ran".
#   2. Do NOT pass --nologo. Under Microsoft.Testing.Platform, dotnet test forwards unrecognised
#      options to the test application, which then prints its help and runs nothing.
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

exec "$DOTNET_ROOT/dotnet" test Canger.slnx "$@"
