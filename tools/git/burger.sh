#!/usr/bin/env bash
# Bash entry point for the verified upstream cherry-pick workflow.
# The implementation is shared with tools/merge.sh so both documented paths
# have identical safety checks, conflict handling, tests, and no-push behavior.
set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
exec bash "$SCRIPT_DIR/../merge.sh" "$@"
