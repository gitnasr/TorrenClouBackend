#!/bin/bash
set -e

# Start the .NET worker
echo "[ENTRYPOINT] Starting TorreClou.Worker..."
exec dotnet TorreClou.Worker.dll
