#!/usr/bin/env bash
# PreToolUse hook for the cloud-line agents. Blocks a file edit outside the paths the agent owns.
#
# The agents work in their own git worktrees, which already stops them editing the integration
# checkout. This guards the other direction: an agent editing a shared member inside its own tree,
# where the change would reach main only through a merge nobody reviewed as a shared change. The
# plan (docs/design/CLOUD-LINES-PLAN.html, section 4) says a shared member changes by request.
#
# Usage: guard-paths.sh <agent>   with the hook's JSON on stdin. Exit 2 blocks the call and the
# message on stderr reaches the agent; anything the script cannot parse is allowed through, so a
# schema change never turns into a silent lockout of the agent's own directory.
set -u
agent="${1:-}"

case "$agent" in
  azure) allowed='src/Clouds/Azure/ docs/azure/ filters/azure.slnf' ;;
  gcp)   allowed='src/Clouds/Gcp/ docs/gcp/ filters/gcp.slnf src/Functions/Hardened.CloudEvents/' ;;
  *)     exit 0 ;;
esac

path=$(python3 -c '
import json, sys
try:
    data = json.load(sys.stdin)
except Exception:
    print(""); sys.exit(0)
tool_input = data.get("tool_input") or {}
for key in ("file_path", "notebook_path", "path"):
    value = tool_input.get(key)
    if isinstance(value, str) and value:
        print(value); break
else:
    print("")
' 2>/dev/null)

[ -z "$path" ] && exit 0

# Relative to the worktree root, whatever the hook's cwd is.
root=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
case "$path" in
  /*) rel="${path#"$root"/}" ;;
  *)  rel="$path" ;;
esac

for prefix in $allowed; do
  case "$rel" in
    "$prefix"*) exit 0 ;;
  esac
done

echo "guard-paths: the $agent agent may not edit '$rel'. It owns: $allowed. A change outside those paths is a shared-change request (SCR) to main; see docs/design/CLOUD-LINES-PLAN.html section 4." >&2
exit 2
