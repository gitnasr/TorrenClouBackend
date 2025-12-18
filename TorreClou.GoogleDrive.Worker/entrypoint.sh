#!/bin/bash
set -e

# Cleanup function to unmount on exit
cleanup() {
    echo "[ENTRYPOINT] Cleaning up rclone mount..."
    if [ -f /tmp/rclone.pid ]; then
        RCLONE_PID=$(cat /tmp/rclone.pid)
        if kill -0 $RCLONE_PID 2>/dev/null; then
            echo "[ENTRYPOINT] Stopping rclone process $RCLONE_PID..."
            kill $RCLONE_PID 2>/dev/null || true
            sleep 2
        fi
    fi
    if mountpoint -q /mnt/backblaze 2>/dev/null; then
        echo "[ENTRYPOINT] Unmounting /mnt/backblaze..."
        fusermount -uz /mnt/backblaze 2>/dev/null || umount -l /mnt/backblaze 2>/dev/null || true
    fi
}

# Register cleanup function
trap cleanup EXIT INT TERM

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
    
    # Clean up any existing mounts or stale processes
    echo "[ENTRYPOINT] Cleaning up any existing mounts..."
    
    # Unmount if already mounted
    if mountpoint -q /mnt/backblaze 2>/dev/null; then
        echo "[ENTRYPOINT] Unmounting existing mount at /mnt/backblaze..."
        fusermount -uz /mnt/backblaze 2>/dev/null || umount -l /mnt/backblaze 2>/dev/null || true
        sleep 2
    fi
    
    # Kill any stale rclone processes for this mount
    pkill -f "rclone.*backblaze.*torrenclo" 2>/dev/null || true
    pkill -f "rclone.*backblaze.*nasrika" 2>/dev/null || true
    sleep 1
    
    # Ensure mount point exists and is empty
    mkdir -p /mnt/backblaze
    
    BUCKET_NAME=${BACKBLAZE_BUCKET:-torrenclo}
    echo "[ENTRYPOINT] Mounting Backblaze B2 bucket: ${BUCKET_NAME}"
    
    # Mount the bucket (read-heavy for Google Drive uploads)
    # Run in background instead of daemon mode to avoid timeout issues
    echo "[ENTRYPOINT] Starting rclone mount in background..."
    nohup rclone mount backblaze:${BUCKET_NAME} /mnt/backblaze \
        --allow-other \
        --vfs-cache-mode full \
        --vfs-read-chunk-size 32M \
        --buffer-size 64M \
        --dir-cache-time 5m \
        --log-level INFO \
        > /tmp/rclone.log 2>&1 &
    
    RCLONE_PID=$!
    echo "[ENTRYPOINT] Rclone process started with PID: $RCLONE_PID"
    
    # Wait for mount to be ready
    echo "[ENTRYPOINT] Waiting for mount to initialize..."
    sleep 5
    
    # Verify mount with retries
    max_retries=10
    retry_count=0
    while [ $retry_count -lt $max_retries ]; do
        # Check if process is still running
        if ! kill -0 $RCLONE_PID 2>/dev/null; then
            echo "[ENTRYPOINT] ERROR: Rclone process died. Check logs:"
            tail -20 /tmp/rclone.log 2>/dev/null || echo "No log file found"
            exit 1
        fi
        
        # Check if mount point is mounted
        if mountpoint -q /mnt/backblaze 2>/dev/null; then
            # Test if we can list the directory
            if timeout 5 ls /mnt/backblaze > /dev/null 2>&1; then
                echo "[ENTRYPOINT] Backblaze B2 mounted successfully at /mnt/backblaze"
                echo "[ENTRYPOINT] Mount verified and accessible"
                break
            else
                echo "[ENTRYPOINT] Mount exists but not accessible, retrying... (attempt $((retry_count + 1))/$max_retries)"
            fi
        else
            echo "[ENTRYPOINT] Mount not ready yet, waiting... (attempt $((retry_count + 1))/$max_retries)"
        fi
        retry_count=$((retry_count + 1))
        sleep 2
    done
    
    if ! mountpoint -q /mnt/backblaze 2>/dev/null; then
        echo "[ENTRYPOINT] ERROR: Failed to mount Backblaze B2 after $max_retries attempts"
        echo "[ENTRYPOINT] Rclone process status:"
        ps aux | grep rclone | grep -v grep || echo "No rclone process found"
        echo "[ENTRYPOINT] Last 20 lines of rclone log:"
        tail -20 /tmp/rclone.log 2>/dev/null || echo "No log file found"
        exit 1
    fi
    
    # Store PID for cleanup on exit
    echo $RCLONE_PID > /tmp/rclone.pid
else
    echo "[ENTRYPOINT] Backblaze credentials not provided, skipping mount"
    echo "[ENTRYPOINT] Set BACKBLAZE_KEY_ID and BACKBLAZE_APP_KEY to enable B2 mount"
fi

# Start the .NET worker
echo "[ENTRYPOINT] Starting TorreClou.GoogleDrive.Worker..."
exec dotnet TorreClou.GoogleDrive.Worker.dll
