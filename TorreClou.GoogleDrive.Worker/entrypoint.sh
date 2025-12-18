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
    
    # Diagnostic information
    echo "[ENTRYPOINT] === FUSE Mount Diagnostics ==="
    echo "[ENTRYPOINT] Current user: $(whoami)"
    echo "[ENTRYPOINT] User ID: $(id -u)"
    echo "[ENTRYPOINT] Group ID: $(id -g)"
    
    # Check FUSE device
    if [ -e /dev/fuse ]; then
        echo "[ENTRYPOINT] FUSE device found: /dev/fuse"
        echo "[ENTRYPOINT] FUSE device permissions: $(ls -l /dev/fuse 2>/dev/null || echo 'Cannot read')"
        if [ -r /dev/fuse ] && [ -w /dev/fuse ]; then
            echo "[ENTRYPOINT] FUSE device is readable and writable"
        else
            echo "[ENTRYPOINT] WARNING: FUSE device permissions may be insufficient"
            # Try to fix permissions (may not work in all environments)
            chmod 666 /dev/fuse 2>/dev/null || echo "[ENTRYPOINT] Could not change FUSE device permissions"
        fi
    else
        echo "[ENTRYPOINT] ERROR: FUSE device not found!"
        echo "[ENTRYPOINT] Docker container must be run with --privileged or --cap-add SYS_ADMIN --device /dev/fuse"
        echo "[ENTRYPOINT] Example: docker run --cap-add SYS_ADMIN --device /dev/fuse ..."
        exit 1
    fi
    
    # Check FUSE configuration
    if [ -f /etc/fuse.conf ]; then
        echo "[ENTRYPOINT] FUSE config file exists: /etc/fuse.conf"
        if grep -q "user_allow_other" /etc/fuse.conf; then
            echo "[ENTRYPOINT] FUSE config: user_allow_other is enabled"
        else
            echo "[ENTRYPOINT] WARNING: user_allow_other not found in /etc/fuse.conf"
        fi
    else
        echo "[ENTRYPOINT] WARNING: /etc/fuse.conf not found"
    fi
    
    # Ensure mount point exists with correct permissions (root-owned for FUSE)
    mkdir -p /mnt/backblaze
    chown root:root /mnt/backblaze
    chmod 755 /mnt/backblaze
    echo "[ENTRYPOINT] Mount point: /mnt/backblaze"
    echo "[ENTRYPOINT] Mount point ownership: $(ls -ld /mnt/backblaze | awk '{print $3":"$4}')"
    echo "[ENTRYPOINT] Mount point permissions: $(ls -ld /mnt/backblaze | awk '{print $1}')"
    
    # Try to load fuse module if modprobe is available (may not work in containers)
    if command -v modprobe >/dev/null 2>&1; then
        echo "[ENTRYPOINT] Attempting to load fuse kernel module..."
        modprobe fuse 2>/dev/null && echo "[ENTRYPOINT] FUSE module loaded" || echo "[ENTRYPOINT] Could not load FUSE module (may already be loaded)"
    fi
    echo "[ENTRYPOINT] === End Diagnostics ==="
    
    BUCKET_NAME=${BACKBLAZE_BUCKET:-torrenclo}
    echo "[ENTRYPOINT] Mounting Backblaze B2 bucket: ${BUCKET_NAME}"
    
    # Mount the bucket (read-heavy for Google Drive uploads)
    # Run in background instead of daemon mode to avoid timeout issues
    # Use --allow-other and --allow-non-empty to handle existing mounts
    echo "[ENTRYPOINT] Starting rclone mount in background..."
    
    # Try mounting with different options
    MOUNT_ATTEMPT=1
    MAX_MOUNT_ATTEMPTS=2
    
    while [ $MOUNT_ATTEMPT -le $MAX_MOUNT_ATTEMPTS ]; do
        echo "[ENTRYPOINT] Mount attempt $MOUNT_ATTEMPT of $MAX_MOUNT_ATTEMPTS..."
        
        if [ $MOUNT_ATTEMPT -eq 1 ]; then
            # First attempt: standard mount with allow-other
            MOUNT_CMD="rclone mount backblaze:${BUCKET_NAME} /mnt/backblaze \
                --allow-other \
                --allow-non-empty \
                --vfs-cache-mode full \
                --vfs-read-chunk-size 32M \
                --buffer-size 64M \
                --dir-cache-time 5m \
                --log-level INFO \
                --umask 0000"
        else
            # Second attempt: without allow-other (in case user_allow_other is not set)
            echo "[ENTRYPOINT] Retrying without --allow-other flag..."
            MOUNT_CMD="rclone mount backblaze:${BUCKET_NAME} /mnt/backblaze \
                --allow-non-empty \
                --vfs-cache-mode full \
                --vfs-read-chunk-size 32M \
                --buffer-size 64M \
                --dir-cache-time 5m \
                --log-level INFO \
                --umask 0000"
        fi
        
        nohup $MOUNT_CMD > /tmp/rclone.log 2>&1 &
        RCLONE_PID=$!
        echo "[ENTRYPOINT] Rclone process started with PID: $RCLONE_PID"
        
        # Wait a moment to see if process dies immediately
        sleep 2
        
        if ! kill -0 $RCLONE_PID 2>/dev/null; then
            echo "[ENTRYPOINT] Rclone process died immediately. Check logs:"
            tail -20 /tmp/rclone.log 2>/dev/null || echo "No log file found"
            
            if [ $MOUNT_ATTEMPT -lt $MAX_MOUNT_ATTEMPTS ]; then
                echo "[ENTRYPOINT] Will retry with different options..."
                MOUNT_ATTEMPT=$((MOUNT_ATTEMPT + 1))
                sleep 1
                continue
            else
                echo "[ENTRYPOINT] All mount attempts failed"
                break
            fi
        else
            echo "[ENTRYPOINT] Rclone process is running, proceeding to verify mount..."
            break
        fi
    done
    
    # Wait for mount to be ready
    echo "[ENTRYPOINT] Waiting for mount to initialize..."
    sleep 5
    
    # Verify mount with retries
    max_retries=10
    retry_count=0
    while [ $retry_count -lt $max_retries ]; do
        # Check if process is still running
        if ! kill -0 $RCLONE_PID 2>/dev/null; then
            echo "[ENTRYPOINT] ERROR: Rclone process died. Diagnostic information:"
            echo "[ENTRYPOINT] === Rclone Process Diagnostics ==="
            echo "[ENTRYPOINT] Process PID: $RCLONE_PID"
            echo "[ENTRYPOINT] Process status: $(ps -p $RCLONE_PID 2>/dev/null || echo 'Process not found')"
            echo "[ENTRYPOINT] Last 30 lines of rclone log:"
            tail -30 /tmp/rclone.log 2>/dev/null || echo "No log file found"
            echo "[ENTRYPOINT] === Troubleshooting Steps ==="
            echo "[ENTRYPOINT] 1. Ensure container is run with: --cap-add SYS_ADMIN --device /dev/fuse"
            echo "[ENTRYPOINT] 2. If that fails, try: --privileged (less secure)"
            echo "[ENTRYPOINT] 3. Check host system FUSE support: lsmod | grep fuse"
            echo "[ENTRYPOINT] 4. Verify Backblaze credentials are correct"
            echo "[ENTRYPOINT] 5. Check Docker daemon logs for security restrictions"
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
        echo "[ENTRYPOINT] === Failure Diagnostics ==="
        echo "[ENTRYPOINT] Rclone process status:"
        ps aux | grep rclone | grep -v grep || echo "No rclone process found"
        echo "[ENTRYPOINT] Mount point status:"
        mountpoint /mnt/backblaze 2>&1 || echo "Mount point check failed"
        echo "[ENTRYPOINT] Last 30 lines of rclone log:"
        tail -30 /tmp/rclone.log 2>/dev/null || echo "No log file found"
        echo "[ENTRYPOINT] === Troubleshooting Steps ==="
        echo "[ENTRYPOINT] 1. Try running with --privileged instead of --cap-add SYS_ADMIN"
        echo "[ENTRYPOINT] 2. Check if AppArmor/SELinux is blocking: dmesg | tail -20"
        echo "[ENTRYPOINT] 3. Verify FUSE is available: ls -l /dev/fuse"
        echo "[ENTRYPOINT] 4. Check FUSE config: cat /etc/fuse.conf"
        echo "[ENTRYPOINT] 5. Ensure user_allow_other is in /etc/fuse.conf"
        echo "[ENTRYPOINT] === Fallback Option ==="
        echo "[ENTRYPOINT] FUSE mount failed. The application will use rclone copy/sync"
        echo "[ENTRYPOINT] for individual file operations instead of a mounted filesystem."
        echo "[ENTRYPOINT] This is slower but will work without FUSE support."
        
        # Clean up failed mount attempt
        if [ -n "$RCLONE_PID" ] && kill -0 $RCLONE_PID 2>/dev/null; then
            kill $RCLONE_PID 2>/dev/null || true
        fi
        
        # Set environment variable to indicate FUSE mount failed
        export FUSE_MOUNT_FAILED=1
        echo "[ENTRYPOINT] Continuing without FUSE mount (fallback mode enabled)"
    else
        # Store PID for cleanup on exit
        echo $RCLONE_PID > /tmp/rclone.pid
        echo "[ENTRYPOINT] FUSE mount successful - normal operation mode"
    fi
else
    echo "[ENTRYPOINT] Backblaze credentials not provided, skipping mount"
    echo "[ENTRYPOINT] Set BACKBLAZE_KEY_ID and BACKBLAZE_APP_KEY to enable B2 mount"
fi

# Start the .NET worker
echo "[ENTRYPOINT] Starting TorreClou.GoogleDrive.Worker..."
exec dotnet TorreClou.GoogleDrive.Worker.dll
