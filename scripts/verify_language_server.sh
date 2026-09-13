#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
server="$repo_root/src/DataGuard.LanguageServer/bin/Release/net9.0/DataGuard.LanguageServer.dll"
if [[ ! -f "$server" ]]; then
  echo "LanguageServer Release artifact is missing; build it first." >&2
  exit 2
fi

SERVER="$server" python3 - <<'PY'
import json
import os
import subprocess
import time

process = subprocess.Popen(["dotnet", os.environ["SERVER"]], stdin=subprocess.PIPE, stdout=subprocess.PIPE)

def send(message):
    payload = json.dumps(message, separators=(",", ":")).encode()
    process.stdin.write(f"Content-Length: {len(payload)}\r\n\r\n".encode() + payload)
    process.stdin.flush()

send({"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {}})
send({"jsonrpc": "2.0", "method": "textDocument/didOpen", "params": {"textDocument": {"uri": "file:///tmp/dataguard-lsp.cs", "version": 1, "text": "SELECT 1"}}})
send({"jsonrpc": "2.0", "method": "textDocument/didChange", "params": {"textDocument": {"uri": "file:///tmp/dataguard-lsp.cs", "version": 2}, "contentChanges": [{"text": "UPDATE t SET c=1"}]}})
time.sleep(0.35)
send({"jsonrpc": "2.0", "method": "textDocument/didClose", "params": {"textDocument": {"uri": "file:///tmp/dataguard-lsp.cs"}}})
process.stdin.close()
output = process.stdout.read()
process.wait(timeout=3)
if process.returncode != 0 or b"DGSQL001" not in output or b'"diagnostics":[]' not in output:
    raise SystemExit("Language Server protocol smoke failed")
print("Language Server initialize/didOpen/didChange/didClose smoke passed")
PY
