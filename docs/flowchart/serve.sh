#!/usr/bin/env bash
# Serves this folder on :8765 and restarts the server whenever it stops.
cd "$(dirname "$0")"
while true; do
  echo "$(date '+%F %T') starting server on http://localhost:8765"
  python3 -m http.server 8765 --bind 127.0.0.1
  echo "$(date '+%F %T') server stopped (exit $?), restarting in 2s"
  sleep 2
done
