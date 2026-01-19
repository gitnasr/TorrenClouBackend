#!/bin/bash
set -e

echo "[ENTRYPOINT] Starting TorreClou.GoogleDrive.Worker..."

# === Validate temp directories exist and are writable ===
if [ ! -d "/app/tmp" ]; then
    echo "[ENTRYPOINT] WARNING: /app/tmp not mounted, creating local directory..."
    mkdir -p /app/tmp/bundle
fi

# === Clean stale temp files on startup ===
echo "[ENTRYPOINT] Cleaning stale temp files..."
find /app/tmp -type f -mtime +1 -delete 2>/dev/null || true
find /app/tmp -type d -empty -delete 2>/dev/null || true

# === Verify disk space ===
AVAIL_KB=$(df /app/tmp 2>/dev/null | tail -1 | awk '{print $4}' || echo "0")
if [ "$AVAIL_KB" -lt 1048576 ] 2>/dev/null; then
    echo "[ENTRYPOINT] WARNING: Less than 1GB available in /app/tmp"
fi

echo "[ENTRYPOINT] Temp directory: $(du -sh /app/tmp 2>/dev/null | cut -f1 || echo 'N/A')"

# Start the .NET worker
exec dotnet TorreClou.GoogleDrive.Worker.dll
