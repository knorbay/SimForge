#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "$0")/.." && pwd)"
version="${1:-0.7.0}"
output_dir="${2:-$repo_dir/artifacts}"
package_name="SimForge-${version}-macOS-arm64"
publish_dir="$(mktemp -d "${TMPDIR:-/tmp}/simforge-publish.XXXXXX")"
app_dir="$output_dir/$package_name/SimForge.app"
zip_path="$output_dir/$package_name.zip"
trap 'rm -rf "$publish_dir"' EXIT

rm -rf "$output_dir/$package_name" "$zip_path"
mkdir -p "$app_dir/Contents/MacOS" "$app_dir/Contents/Resources"

dotnet publish "$repo_dir/SimForge/SimForge.csproj" \
  --configuration Release \
  --runtime osx-arm64 \
  --self-contained true \
  --output "$publish_dir" \
  -p:UsedAvaloniaProducts= \
  -p:DebugType=None \
  -p:DebugSymbols=false

cp -R "$publish_dir/." "$app_dir/Contents/MacOS/"
cp "$repo_dir/SimForge/Assets/SimForge.icns" "$app_dir/Contents/Resources/SimForge.icns"
sed "s/@VERSION@/$version/g" "$repo_dir/packaging/Info.plist.in" > "$app_dir/Contents/Info.plist"
chmod +x "$app_dir/Contents/MacOS/SimForge"

codesign --force --deep --sign - "$app_dir"
codesign --verify --deep --strict "$app_dir"
plutil -lint "$app_dir/Contents/Info.plist"

(cd "$output_dir/$package_name" && zip -qry "$zip_path" "SimForge.app")
echo "$zip_path"
