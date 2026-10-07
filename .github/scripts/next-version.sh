#!/usr/bin/env bash
# Prints the version that follows <latest-tag> for a <level> of major, minor or patch.
# Without a previous tag the first release is 0.1.0.
#   next-version.sh v1.4.2 minor   -> 1.5.0
#   next-version.sh "" patch       -> 0.1.0
set -euo pipefail

latest=${1:-}
level=${2:?usage: next-version.sh <latest-tag> <major|minor|patch>}

if [[ -z $latest ]]; then
  echo 0.1.0
  exit 0
fi
if [[ ! $latest =~ ^v([0-9]+)\.([0-9]+)\.([0-9]+)$ ]]; then
  echo "not a vX.Y.Z tag: $latest" >&2
  exit 1
fi
major=${BASH_REMATCH[1]} minor=${BASH_REMATCH[2]} patch=${BASH_REMATCH[3]}

case $level in
  major) echo "$((major + 1)).0.0" ;;
  minor) echo "$major.$((minor + 1)).0" ;;
  patch) echo "$major.$minor.$((patch + 1))" ;;
  *) echo "unknown level: $level (expected major, minor or patch)" >&2; exit 1 ;;
esac
