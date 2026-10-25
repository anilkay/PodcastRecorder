#!/usr/bin/env bash
# Wraps a macOS publish of the Avalonia app into "Podcast Recorder.app", so it opens with a double
# click, has the app icon and asks for the microphone by name. Runs on macOS (iconutil, codesign).
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

# The icon from the same PNGs as the Windows icon (the largest is 256 px).
iconset=$(mktemp -d)/AppIcon.iconset
mkdir -p "$iconset"
cp "$logo/16.png" "$iconset/icon_16x16.png"
cp "$logo/32.png" "$iconset/icon_16x16@2x.png"
cp "$logo/32.png" "$iconset/icon_32x32.png"
cp "$logo/64.png" "$iconset/icon_32x32@2x.png"
cp "$logo/128.png" "$iconset/icon_128x128.png"
cp "$logo/256.png" "$iconset/icon_128x128@2x.png"
cp "$logo/256.png" "$iconset/icon_256x256.png"
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"

# Sign every native library and the executable first, then the bundle that seals them.
find "$app/Contents/MacOS" -type f -print0 | while IFS= read -r -d '' file; do
    if file -b "$file" | grep -q '^Mach-O'; then codesign --force --sign - --timestamp=none "$file"; fi
done
codesign --force --sign - --timestamp=none "$app"
codesign --verify --strict --verbose=2 "$app"
