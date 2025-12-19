#!/bin/bash
set -e

# Mount Backblaze B2 using rclone if credentials are provided
if [ -n "$BACKBLAZE_KEY_ID" ] && [ -n "$BACKBLAZE_APP_KEY" ]; then
    echo "[ENTRYPOINT] Configuring rclone for Backblaze B2..."
    
    # Configure rclone
    mkdir -p /root/.config/rclone
    cat > /root/.config/rclone/rclone.conf << EOF
[backblaze]
type = b2
account = ${BACKBLAZE_KEY_ID}
key = ${BACKBLAZE_APP_KEY}
EOF
    
    echo "[ENTRYPOINT] Mounting Backblaze B2 bucket: ${BACKBLAZE_BUCKET:-torrenclo}"
    
    # Mount the bucket with cache mode off - files write directly to B2 without local caching
    # This eliminates local disk usage entirely
    rclone mount backblaze:${BACKBLAZE_BUCKET:-torrenclo} /mnt/backblaze \
        --allow-other \
        --vfs-cache-mode off \
        --buffer-size 64M \
        --dir-cache-time 5m \
        --log-level INFO \
        --daemon
    
    # Wait for mount to be ready
    sleep 3
    
    # Verify mount
    if mountpoint -q /mnt/backblaze; then
        echo "[ENTRYPOINT] Backblaze B2 mounted successfully at /mnt/backblaze"
    else
        echo "[ENTRYPOINT] WARNING: Backblaze B2 mount may have failed, check logs"
    fi
else
    echo "[ENTRYPOINT] Backblaze credentials not provided, skipping mount"
    echo "[ENTRYPOINT] Set BACKBLAZE_KEY_ID and BACKBLAZE_APP_KEY to enable B2 mount"
fi

# Start the .NET worker
echo "[ENTRYPOINT] Starting TorreClou.Worker..."
exec dotnet TorreClou.Worker.dll
