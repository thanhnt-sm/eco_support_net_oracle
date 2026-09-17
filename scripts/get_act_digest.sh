#!/usr/bin/env bash
# Re-resolve the pinned digest for scripts/act-runner.Dockerfile.
# Usage: ./scripts/get_act_digest.sh [tag]
# Prints the Docker-Content-Digest for catthehacker/ubuntu:<tag>.
set -euo pipefail

TAG="${1:-act-latest}"
REPO="catthehacker/ubuntu"

TOKEN="$(curl -fsSL "https://auth.docker.io/token?service=registry.docker.io&scope=repository:${REPO}:pull" \
    | python3 -c 'import json,sys; print(json.load(sys.stdin)["token"])')"
curl -fsSL -D - -o /dev/null \
    -H 'Accept: application/vnd.docker.distribution.manifest.list.v2+json' \
    -H "Authorization: Bearer ${TOKEN}" \
    "https://registry-1.docker.io/v2/${REPO}/manifests/${TAG}" \
    | grep -i docker-content-digest
