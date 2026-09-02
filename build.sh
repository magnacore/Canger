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
#   ./build.sh deb             A Debian package
#   ./build.sh appimage        One executable file, needing only FUSE
#   ./build.sh release         Check, build all four artifacts, and publish them to GitHub
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
            # Generated from the binary being packaged, not copied from doc/. The committed
            # copy is generated too, and went three releases without being regenerated -- the
            # tarballs shipped a manual headed "canger 0.3.0" describing neither removable drives
            # nor version control, while the .deb, which already generated its own, was correct.
            # A test keeps the committed copy honest; this makes the tarball independent of it.
            mkdir -p "$staging/doc"
            if ! ( cd "$staging" && ./canger --clean --man ) > "$staging/doc/canger.1"; then
                echo "canger: could not generate the manual page from the packaged binary" >&2
                exit 1
            fi

            # Run what is about to be shipped. A publish that emits a broken assembly still
            # reports success, so the only way to know the tarball is worth handing to anyone is
            # to start the thing. `--version` touches the host, the runtime and managed startup,
            # which is all that failure mode needs.
            if ! ( cd "$staging" && ./canger --version >/dev/null 2>&1 ); then
                echo "canger: $name does not start; refusing to package it" >&2
                exit 1
            fi

            # lzip rather than gzip: smaller, and its container carries a CRC of the
            # uncompressed data plus the original size, so a truncated or corrupted archive is
            # detected rather than silently unpacked short. The cost is that the recipient needs
            # lzip installed — `tar xf` alone will not do it — which is why the .deb and the
            # AppImage exist for people who would rather not.
            command -v lzip >/dev/null || {
                echo "canger: lzip is not installed; the tarballs need it (apt install lzip)" >&2
                exit 1
            }

            tar --lzip -cf "$out/$name.tar.lz" -C "$out" "$name"
            rm -rf "$staging"
        done

        rm -rf "$artifacts"

        echo
        ls -lh "$out"/*.tar.lz | awk '{ printf "  %-52s %s\n", $9, $5 }'
        echo
        echo "  tar --lzip -xf <file>, then run ./canger. The directory has to stay together:"
        echo "  config/ holds the key bindings, and doc/canger.1 installs as man canger."
        ;;
    deb)
        # A Debian package, because a tarball cannot do the three things that make a terminal
        # program feel installed: put `canger` on the PATH, put the manual where `man canger`
        # finds it, and be removable. Debian packages no .NET runtime at all — `apt-cache search
        # ^dotnet-runtime` comes back empty — so a framework-dependent package would depend on
        # something that does not exist outside Microsoft's own apt repository. Self-contained is
        # larger and it works, which is the whole point of a package.
        version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)
        rid="${CANGER_RID:-linux-x64}"
        out="dist"

        [ -n "$version" ] || { echo "canger: no <Version> in Directory.Build.props" >&2; exit 1; }

        case "$rid" in
            linux-x64)   arch=amd64 ;;
            linux-arm64) arch=arm64 ;;
            *) echo "canger: no Debian architecture known for $rid" >&2; exit 1 ;;
        esac

        name="canger_${version}_${arch}"
        staging="$out/$name"
        lib="$staging/usr/lib/canger"

        mkdir -p "$out"
        rm -rf "$staging"
        mkdir -p "$lib" "$staging/usr/bin" "$staging/usr/share/man/man1" \
                 "$staging/usr/share/doc/canger" "$staging/DEBIAN"

        echo "canger: building $name.deb"

        lean="-p:SatelliteResourceLanguages=en -p:DebugType=none -p:GenerateDocumentationFile=false"

        # shellcheck disable=SC2086
        "$DOTNET_ROOT/dotnet" publish src/Canger.App/Canger.App.csproj \
            -c Release -r "$rid" --self-contained true \
            --artifacts-path "$out/.artifacts/deb" \
            -o "$lib" $lean "$@" >/dev/null

        [ -f "$lib/config/cc.conf" ] || {
            echo "canger: $lib/config/cc.conf is missing; the package would have no key bindings" >&2
            exit 1
        }

        cp LICENSE "$staging/usr/share/doc/canger/copyright"
        cp README.md "$staging/usr/share/doc/canger/"

        # `canger` on the PATH as a symlink rather than a wrapper script. A .NET apphost finds
        # its own directory through /proc/self/exe, which resolves the link, so the shipped
        # config beside it is still found — verified rather than assumed.
        ln -sf ../lib/canger/canger "$staging/usr/bin/canger"

        # Generated from the binary being packaged rather than copied from `doc/`, so the manual
        # in the package describes the version in the package.
        # --clean, or the manual documents whoever built the package. `--man` renders the key
        # bindings and commands that are actually loaded, and without this the binary reads
        # ~/.config/canger on the way past: the 0.6.0 .deb shipped a manual describing the
        # maintainer's own `efc`, `fzf_locate` and `file_convert_text`, and not the defaults.
        "$lib/canger" --clean --man | gzip -9n > "$staging/usr/share/man/man1/canger.1.gz"

        # Read off the binaries rather than guessed at, and rather than computed: dpkg-shlibdeps
        # wants the whole debhelper build tree around it and produces nothing useful without it.
        # What every bundled binary links against, minus what is bundled, is:
        #
        #   libc.so.6 libm.so.6 libdl.so.2 libpthread.so.0 librt.so.1   -> libc6
        #   libgcc_s.so.1                                               -> libgcc-s1
        #   libstdc++.so.6                                              -> libstdc++6
        #   liblttng-ust.so.0    tracing, loaded only if asked for      -> not depended on
        #
        # ICU appears in none of them because .NET opens it by name at runtime rather than
        # linking it. It is still required: with InvariantGlobalization off, a self-contained
        # build exits at startup without it. The alternatives span current Debian and Ubuntu.
        depends="libc6, libgcc-s1, libstdc++6"
        depends="$depends, libicu76 | libicu74 | libicu72 | libicu71 | libicu70"

        # OpenSSL is opened the same way and only when something asks for cryptography, which
        # Canger itself never does — so it is recommended rather than required, and a plugin that
        # wants it will find it on any system that has not gone out of its way.
        recommends="libssl3t64 | libssl3"

        size=$(du -sk "$staging/usr" | cut -f1)

        cat > "$staging/DEBIAN/control" <<EOF
Package: canger
Version: $version
Section: utils
Priority: optional
Architecture: $arch
Maintainer: $(git config user.name) <$(git config user.email)>
Installed-Size: $size
Depends: $depends
Recommends: $recommends
Description: file manager for the terminal with vi-style key bindings
 Canger shows a directory as a set of columns: the path leading to where you
 are, the listing itself, and a preview of whatever the cursor is on. Almost
 everything is a command and almost every key is bound to one.
 .
 It is a port of ranger from Python to .NET, aiming at a 1-to-1 match of its
 key bindings, commands, settings and configuration syntax. It ships rifle,
 ranger's file launcher, and reads the same rc.conf, rifle.conf and scope.sh.
 .
 The .NET runtime is included, so nothing else needs installing.
EOF

        # Run what is about to be packaged. A publish that emits a broken assembly still reports
        # success, so starting it is the only way to know the package is worth handing to anyone.
        if ! "$lib/canger" --version >/dev/null 2>&1; then
            echo "canger: the packaged build does not start; refusing to package it" >&2
            exit 1
        fi

        dpkg-deb --build --root-owner-group "$staging" "$out/$name.deb" >/dev/null
        rm -rf "$staging" "$out/.artifacts"

        echo
        ls -lh "$out/$name.deb" | awk '{ printf "  %-52s %s\n", $9, $5 }'
        echo
        echo "  sudo apt install ./$out/$name.deb"
        echo "  Puts canger on the PATH, the manual where man canger finds it, and the shipped"
        echo "  cc.conf, rifle.conf and scope.sh under /usr/lib/canger/config."
        ;;
    appimage)
        # One file to hand to anybody, which is the only thing this does that the self-contained
        # tarball does not: both bundle the .NET runtime, and neither bundles what Canger actually
        # reaches for at runtime — less, file, git, udisksctl, the user's editor — because those
        # belong to the machine it is running on.
        #
        # The cost is that the recipient needs FUSE to run it at all, or has to know about
        # --appimage-extract-and-run. The .deb and the tarball have no such requirement, so this
        # is the convenient artifact rather than the compatible one.
        #
        # Which FUSE was worth checking rather than repeating: this appimagetool builds a
        # type2-runtime, which statically bundles libfuse and squashfuse and needs `fusermount3`
        # from the `fuse3` package plus /dev/fuse. Not `libfuse2`, which is the requirement people
        # remember and the one that has been dropped from recent Debian and Ubuntu — measured by
        # running the image with fusermount taken off the PATH and watching it fail.
        version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)
        rid="${CANGER_RID:-linux-x64}"
        out="dist"

        [ -n "$version" ] || { echo "canger: no <Version> in Directory.Build.props" >&2; exit 1; }

        command -v appimagetool >/dev/null || {
            echo "canger: appimagetool is not on the PATH." >&2
            echo "canger: Debian does not package it; take the x86_64 build from" >&2
            echo "canger:   https://github.com/AppImage/appimagetool/releases" >&2
            exit 1
        }

        case "$rid" in
            linux-x64)   arch=x86_64 ;;
            linux-arm64) arch=aarch64 ;;
            *) echo "canger: no AppImage architecture known for $rid" >&2; exit 1 ;;
        esac

        appdir="$out/Canger.AppDir"
        image="$out/Canger-$version-$arch.AppImage"

        mkdir -p "$out"
        rm -rf "$appdir"
        mkdir -p "$appdir/usr/bin" "$appdir/usr/share/applications" \
                 "$appdir/usr/share/icons/hicolor/256x256/apps" "$appdir/usr/share/man/man1"

        echo "canger: building $(basename "$image")"

        lean="-p:SatelliteResourceLanguages=en -p:DebugType=none -p:GenerateDocumentationFile=false"

        # Into usr/bin, because `config/` has to sit beside the binary: Canger finds its shipped
        # configuration from the directory the executable is in, whatever that turns out to be.
        # shellcheck disable=SC2086
        "$DOTNET_ROOT/dotnet" publish src/Canger.App/Canger.App.csproj \
            -c Release -r "$rid" --self-contained true \
            --artifacts-path "$out/.artifacts/appimage" \
            -o "$appdir/usr/bin" $lean "$@" >/dev/null

        [ -f "$appdir/usr/bin/config/cc.conf" ] || {
            echo "canger: $appdir/usr/bin/config/cc.conf is missing; the image would have no key bindings" >&2
            exit 1
        }

        cp LICENSE README.md "$appdir/usr/bin/"
        "$appdir/usr/bin/canger" --man | gzip -9n > "$appdir/usr/share/man/man1/canger.1.gz"

        # `readlink -f` because AppRun is invoked through whatever name the user gave the image,
        # and $0 is that name rather than the path inside the mounted image.
        cat > "$appdir/AppRun" <<'APPRUN'
#!/bin/sh
here=$(dirname "$(readlink -f "$0")")
exec "$here/usr/bin/canger" "$@"
APPRUN
        chmod +x "$appdir/AppRun"

        # Terminal=true is the whole difference between this and a desktop program: launched from
        # a menu it needs a terminal opened for it, and without saying so it would flash and die.
        cat > "$appdir/canger.desktop" <<'DESKTOP'
[Desktop Entry]
Type=Application
Name=Canger
GenericName=File Manager
Comment=File manager for the terminal with vi-style key bindings
Exec=canger %f
Icon=canger
Terminal=true
Categories=System;FileTools;FileManager;ConsoleOnly;
Keywords=file;manager;ranger;terminal;vi;
DESKTOP

        cp "$appdir/canger.desktop" "$appdir/usr/share/applications/canger.desktop"
        cp doc/canger.png "$appdir/canger.png"
        cp doc/canger.png "$appdir/usr/share/icons/hicolor/256x256/apps/canger.png"

        if command -v desktop-file-validate >/dev/null; then
            desktop-file-validate "$appdir/canger.desktop" || {
                echo "canger: the desktop entry is not valid; refusing to package it" >&2
                exit 1
            }
        fi

        # Run what is about to be packaged, before packaging it.
        if ! "$appdir/usr/bin/canger" --version >/dev/null 2>&1; then
            echo "canger: the packaged build does not start; refusing to package it" >&2
            exit 1
        fi

        rm -f "$image"
        ARCH="$arch" appimagetool "$appdir" "$image" >/dev/null 2>&1 || {
            echo "canger: appimagetool failed" >&2
            exit 1
        }

        rm -rf "$appdir" "$out/.artifacts"

        # And run the image itself, which is the only thing that proves the AppRun, the layout
        # and the runtime all agree.
        if ! "$image" --version >/dev/null 2>&1; then
            echo "canger: $image does not start; FUSE is needed to run one at all" >&2
            exit 1
        fi

        echo
        ls -lh "$image" | awk '{ printf "  %-52s %s\n", $9, $5 }'
        echo
        echo "  One file, already executable. Needs fuse3 on the machine it runs on, or"
        echo "  ./Canger-$version-$arch.AppImage --appimage-extract-and-run"
        ;;
    release)
        # Publishing, which is not a build step and is deliberately not part of `dist`. `dist` is
        # run to inspect a package or to try a change; doing it here as a side effect would have
        # shipped 0.7.0 on the day its cursor bug was found and fixed, because the artifacts were
        # built before the defect was.
        #
        # The value of this target is the refusals, not the upload. Every one of them is a mistake
        # that has actually happened: a manual that documented the maintainer's own key bindings
        # rather than the defaults and shipped in the 0.6.0 .deb; a doc/canger.1 three releases
        # stale; artifacts built at one version while the tag said another.
        version=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)
        [ -n "$version" ] || { echo "canger: no <Version> in Directory.Build.props" >&2; exit 1; }

        dry_run=false
        case "${1:-}" in --dry-run) dry_run=true; shift ;; esac

        refuse() { echo "canger: $1" >&2; exit 1; }

        # 1. Nothing uncommitted. What is published has to be a commit somebody can check out.
        [ -z "$(git status --porcelain)" ] || refuse "the working tree is dirty; commit or stash first"

        # 2. On a tag, and the tag is the version being built. These drifted apart once already.
        tag=$(git describe --exact-match --tags HEAD 2>/dev/null) \
            || refuse "HEAD is not tagged; tag the release commit first"
        [ "$tag" = "v$version" ] \
            || refuse "tag $tag does not match <Version> $version in Directory.Build.props"

        command -v gh >/dev/null 2>&1 || refuse "gh is not installed; see https://cli.github.com"
        gh auth status >/dev/null 2>&1 || refuse "gh is not logged in; run: gh auth login"

        if gh release view "$tag" >/dev/null 2>&1; then
            refuse "$tag is already released; delete it first if you mean to replace it"
        fi

        echo "  releasing $tag"
        rm -rf dist
        "$0" publish >/dev/null
        for artifact in dist deb appimage; do
            echo "  building $artifact"
            "$0" "$artifact" >/dev/null
        done

        # 3. Everything that carries a version agrees with the tag. A packaged binary reporting a
        # different number from its own package is the kind of thing nobody notices for a release.
        published="src/Canger.App/bin/Release/net10.0/${CANGER_RID:-linux-x64}/publish/canger"
        [ "$("$published" --version)" = "canger $version" ] \
            || refuse "the built binary does not report $version"
        [ "$(dpkg-deb -f "dist/canger_${version}_amd64.deb" Version)" = "$version" ] \
            || refuse "the .deb metadata does not say $version"

        # 4. The manual describes the program, not the machine it was built on. Compared against
        # what a configuration-free run produces, rather than grepping for whatever leaked last
        # time: the 0.6.0 .deb shipped a manual documenting `efc` and `fzf_locate`, because `--man`
        # renders the bindings actually loaded and the binary had read ~/.config/canger.
        expected_manual=$(mktemp); packaged_manual=$(mktemp)
        trap 'rm -f "$expected_manual" "$packaged_manual"' EXIT
        "$published" --clean --man > "$expected_manual"
        tar --lzip -xOf "dist/canger-$version-linux-x64.tar.lz" --wildcards '*/doc/canger.1' \
            > "$packaged_manual"
        cmp -s "$expected_manual" "$packaged_manual" \
            || refuse "the packaged manual is not what a configuration-free run produces"

        # 5. The AppImage starts. `dist` and `deb` already run what they package; this one needs
        # FUSE, so it can fail on a machine where the other three are fine.
        [ "$(./dist/Canger-$version-x86_64.AppImage --version 2>/dev/null)" = "canger $version" ] \
            || refuse "the AppImage does not start or reports the wrong version"

        echo
        ls -lh dist/ | awk 'NR > 1 { printf "  %-52s %s\n", $9, $5 }'
        echo

        if $dry_run; then
            echo "  --dry-run: checks passed, nothing published. To publish:"
            echo "    gh release create $tag dist/* --title \"Canger $version\" --notes-from-tag"
            exit 0
        fi

        gh release create "$tag" dist/* --title "Canger $version" --notes-from-tag
        ;;
    Debug|Release)
        exec "$DOTNET_ROOT/dotnet" build Canger.slnx -c "$target" "$@"
        ;;
    *)
        # Anything else is passed straight through, so `./build.sh --no-restore` still works.
        exec "$DOTNET_ROOT/dotnet" build Canger.slnx "$target" "$@"
        ;;
esac
