#!/usr/bin/env bash
set -euo pipefail

publish_dir="src/WindowsTriage.App/bin/Release/net10.0-windows10.0.22000.0/win-x64/publish"
exe="${publish_dir}/WindowsTriage.exe"

if [[ ! -f "${exe}" ]]; then
  echo "Missing ${exe}. Run dotnet publish first." >&2
  exit 1
fi

sha256sum "${exe}" | tee "${publish_dir}/WindowsTriage.exe.sha256"
