#!/bin/zsh
# Local-only convenience launcher: rebuild derived data, then open the dashboard.
set -e
tool_dir="${0:A:h}"
node "${tool_dir}/scan.mjs"
open "${tool_dir}/dashboard.html"
