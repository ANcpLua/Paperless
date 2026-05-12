#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname -- "$0")/.."
exec ./build.sh Verify \
    --coverage-min-line 95 \
    --coverage-min-branch 75 \
    --coverage-format markdown \
    --coverage-exclude-generated-param true \
    "$@"
