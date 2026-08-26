#!/usr/bin/env bash
# Builds the Canger solution.
#
# The SDK is found on PATH, or at the path it lives at on the machine Canger was written on.
# Set CANGER_DOTNET_ROOT if yours is somewhere else again.
#
#   ./build.sh                 Debug build, for development and for `dotnet test`
#   ./build.sh Release         Release build
#   ./build.sh publish         Release + ReadyToRun, which is what canger.sh prefers to run
#   ./build.sh dist            Release tarballs to hand to somebody else
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
    dist)
        # Two tarballs, because there are two kinds of recipient: one who already has .NET 10 and
        # wants 11MB, and one who has nothing and would rather download 150MB than install a
        # runtime first. Both are ReadyToRun, both carry `config/`, and neither carries the
        # 26MB of symbols, API documentation and Roslyn translations that `publish` leaves in.
        version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)
        rid="${CANGER_RID:-linux-x64}"
        out="dist"

        [ -n "$version" ] || { echo "canger: no <Version> in Directory.Build.props" >&2; exit 1; }

        rm -rf "$out"
        mkdir -p "$out"

        # Each variant gets its own intermediates. Sharing `obj/` between a self-contained and a
        # framework-dependent publish produces assemblies that abort on startup with no message
        # at all — the ReadyToRun images left behind by one are compiled against the runtime the
        # other does not have. It is silent, it depends on what happened to be built last, and
        # the artifact looks perfectly normal, so this is not a tidiness measure.
        artifacts="$out/.artifacts"

        # Trimming what nothing reads at runtime. `SatelliteResourceLanguages` alone is thirteen
        # Roslyn translation directories at around half a megabyte each.
        lean="-p:SatelliteResourceLanguages=en -p:DebugType=none -p:GenerateDocumentationFile=false"

        for kind in runtime portable; do
            case "$kind" in
                runtime)  selfcontained=false; name="canger-$version-$rid" ;;
                portable) selfcontained=true;  name="canger-$version-$rid-selfcontained" ;;
            esac

            staging="$out/$name"
            echo "canger: building $name"

            # shellcheck disable=SC2086
            "$DOTNET_ROOT/dotnet" publish src/Canger.App/Canger.App.csproj \
                -c Release -r "$rid" --self-contained "$selfcontained" \
                --artifacts-path "$artifacts/$kind" \
                -o "$staging" $lean "$@" >/dev/null

            # Without `config/` there are no key bindings at all — not a degraded Canger, an inert
            # one — and it is the one part of the payload that comes from a content file rather
            # than from a project reference, so it is the one that can quietly go missing.
            [ -f "$staging/config/cc.conf" ] || {
                echo "canger: $staging/config/cc.conf is missing; the build would have no key bindings" >&2
                exit 1
            }

            # The licence is an obligation, not a courtesy: Canger is GPL-3.0-or-later, being a
            # port of ranger, so a binary handed to anyone has to say so and the corresponding
            # source has to be available to them. README.md names where.
            cp LICENSE README.md "$staging/"
            mkdir -p "$staging/doc"
            cp doc/canger.1 "$staging/doc/"

            # Run what is about to be shipped. A publish that emits a broken assembly still
            # reports success, so the only way to know the tarball is worth handing to anyone is
            # to start the thing. `--version` touches the host, the runtime and managed startup,
            # which is all that failure mode needs.
            if ! ( cd "$staging" && ./canger --version >/dev/null 2>&1 ); then
                echo "canger: $name does not start; refusing to package it" >&2
                exit 1
            fi

            tar -czf "$out/$name.tar.gz" -C "$out" "$name"
            rm -rf "$staging"
        done

        rm -rf "$artifacts"

        echo
        ls -lh "$out"/*.tar.gz | awk '{ printf "  %-52s %s\n", $9, $5 }'
        echo
        echo "  Unpack and run ./canger. The directory has to stay together: config/ holds the"
        echo "  key bindings, and doc/canger.1 is what ? -> m opens."
        ;;
    Debug|Release)
        exec "$DOTNET_ROOT/dotnet" build Canger.slnx -c "$target" "$@"
        ;;
    *)
        # Anything else is passed straight through, so `./build.sh --no-restore` still works.
        exec "$DOTNET_ROOT/dotnet" build Canger.slnx "$target" "$@"
        ;;
esac
