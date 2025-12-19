#!/bin/bash
set -e

# Start the .NET worker
echo "[ENTRYPOINT] Starting TorreClou.Sync.Worker..."
exec dotnet TorreClou.Sync.Worker.dll

