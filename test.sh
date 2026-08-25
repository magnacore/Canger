#!/usr/bin/env bash
# Runs the Canger test suite.
#
# Two things to know before changing this:
#   1. DOTNET_ROOT must be exported. The SDK is not in a default location, and without it the
#      test apphosts fail to launch and the failure surfaces only as "Zero tests ran".
#   2. Do NOT pass --nologo. Under Microsoft.Testing.Platform, dotnet test forwards unrecognised
#      options to the test application, which then prints its help and runs nothing.
set -euo pipefail

export DOTNET_ROOT="${CANGER_DOTNET_ROOT:-/opt/anaconda3/envs/dotnet/lib/dotnet}"
cd "$(dirname "$0")"

exec "$DOTNET_ROOT/dotnet" test Canger.slnx "$@"
