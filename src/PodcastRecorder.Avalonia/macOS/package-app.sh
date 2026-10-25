#!/usr/bin/env bash
# Wraps a macOS publish of the Avalonia app into "Podcast Recorder.app", so it opens with a double
# click, has the app icon and asks for the microphone by name. Runs on macOS (iconutil, codesign,
# and rsvg-convert from "brew install librsvg").
#
#   bash src/PodcastRecorder.Avalonia/macOS/package-app.sh <publish folder> <version> <output folder>
#
# The bundle is signed ad hoc (no Apple Developer account): Apple Silicon needs a signature to run
# at all, and macOS keeps the microphone permission for the bundle. It is not notarized, so a
# downloaded copy has to be allowed once (see the README).
set -euo pipefail

publish=$1
version=$2
out=$3
here=$(cd "$(dirname "$0")" && pwd)
logo=$(cd "$here/../../../assets/logo/png" && pwd)
app="$out/Podcast Recorder.app"

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"

cp "$here/Info.plist" "$app/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$version" "$app/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$version" "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"

# The icon: up to 256 px from the PNGs of the Windows icon (the small ones are drawn by hand), the
# larger sizes from the SVG. rsvg-convert comes with "brew install librsvg".
iconset=$(mktemp -d)/AppIcon.iconset
mkdir -p "$iconset"
cp "$logo/16.png" "$iconset/icon_16x16.png"
cp "$logo/32.png" "$iconset/icon_16x16@2x.png"
cp "$logo/32.png" "$iconset/icon_32x32.png"
cp "$logo/64.png" "$iconset/icon_32x32@2x.png"
cp "$logo/128.png" "$iconset/icon_128x128.png"
cp "$logo/256.png" "$iconset/icon_128x128@2x.png"
cp "$logo/256.png" "$iconset/icon_256x256.png"
rsvg-convert -w 512 -h 512 -o "$iconset/icon_256x256@2x.png" "$logo/../logo.svg"
cp "$iconset/icon_256x256@2x.png" "$iconset/icon_512x512.png"
rsvg-convert -w 1024 -h 1024 -o "$iconset/icon_512x512@2x.png" "$logo/../logo.svg"
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"

# macOS treats every file in Contents/MacOS as code, the .NET assemblies too, so each one is signed
# (a non-Mach-O file keeps its signature in extended attributes, which ditto's zip preserves). The
# main executable is signed last, with the bundle.
executable="$app/Contents/MacOS/PodcastRecorder.Avalonia"
find "$app/Contents/MacOS" -type f ! -path "$executable" -print0 |
    xargs -0 codesign --force --sign - --timestamp=none
codesign --force --sign - --timestamp=none "$app"
codesign --verify --strict --verbose=2 "$app"
