#!/bin/bash
set -e

# Start the .NET worker
echo "[ENTRYPOINT] Starting TorreClou.GoogleDrive.Worker..."
exec dotnet TorreClou.GoogleDrive.Worker.dll
