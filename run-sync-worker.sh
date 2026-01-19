#!/bin/bash
# Script to run the TorreClou Sync Worker Docker container
# This is a SELF-CONTAINED script - run only this file to start the worker
# Usage: ./run-sync-worker.sh

set -e

# ============================================
# CONFIGURATION
# ============================================
IMAGE_NAME="torreclou-sync-worker"
CONTAINER_NAME="sync-worker"
EXTERNAL_STORAGE="/mnt/torrenclou"
DOCKER_DATA_DIR="${EXTERNAL_STORAGE}/docker/${CONTAINER_NAME}"

# Environment variables (use defaults or override from environment)
BACKBLAZE_KEY_ID="${BACKBLAZE_KEY_ID:-0036961fa7da1a20000000007}"
BACKBLAZE_APP_KEY="${BACKBLAZE_APP_KEY:-K003rx1yKtR0hARydpAqonh7pP8eJRE}"
BACKBLAZE_BUCKET="${BACKBLAZE_BUCKET:-nasrika}"

# Resource limits
MEMORY_LIMIT="4g"
MEMORY_RESERVATION="512m"

# Log rotation
LOG_MAX_SIZE="100m"
LOG_MAX_FILE="3"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

# ============================================
# PRE-FLIGHT CHECKS
# ============================================
echo -e "${GREEN}=== TorreClou Sync Worker Startup ===${NC}"

# Check external storage is mounted
if [ ! -d "$EXTERNAL_STORAGE" ]; then
    echo -e "${RED}ERROR: External storage not found at $EXTERNAL_STORAGE${NC}"
    echo -e "${RED}Please ensure the disk is mounted before running this script.${NC}"
    exit 1
fi

# Create required directories on external storage (prevents root FS fill)
echo -e "${YELLOW}Preparing directories on external storage (requires sudo)...${NC}"
sudo mkdir -p "${DOCKER_DATA_DIR}/tmp"
sudo mkdir -p "${DOCKER_DATA_DIR}/logs"
sudo mkdir -p "${DOCKER_DATA_DIR}/data"
# Set to 777 to ensure the non-root container user (appuser) can write to these volumes
sudo chmod 777 "${DOCKER_DATA_DIR}/tmp" "${DOCKER_DATA_DIR}/logs" "${DOCKER_DATA_DIR}/data"

# Clean stale temp files older than 1 day
echo -e "${YELLOW}Cleaning stale temp files...${NC}"
sudo find "${DOCKER_DATA_DIR}/tmp" -type f -mtime +1 -delete 2>/dev/null || true
sudo find "${DOCKER_DATA_DIR}/tmp" -type d -empty -delete 2>/dev/null || true

# Report disk usage
echo -e "${GREEN}External storage usage: $(df -h $EXTERNAL_STORAGE | tail -1 | awk '{print $3 "/" $2 " (" $5 ")"})${NC}"

# ============================================
# CONTAINER MANAGEMENT
# ============================================

# Check if container already exists and is running
if [ "$(docker ps -q -f name=$CONTAINER_NAME)" ]; then
    echo -e "${YELLOW}Container $CONTAINER_NAME is already running. Stopping it first...${NC}"
    docker stop $CONTAINER_NAME
    docker rm $CONTAINER_NAME
elif [ "$(docker ps -aq -f name=$CONTAINER_NAME)" ]; then
    echo -e "${YELLOW}Removing existing stopped container $CONTAINER_NAME...${NC}"
    docker rm $CONTAINER_NAME
fi

# Check if image exists, if not, build it
if ! docker images | grep -q "^$IMAGE_NAME "; then
    echo -e "${YELLOW}Image $IMAGE_NAME not found. Building it...${NC}"
    docker build -f TorreClou.Sync.Worker/Dockerfile -t $IMAGE_NAME .
fi

# ============================================
# RUN CONTAINER
# ============================================
echo -e "${GREEN}Starting container $CONTAINER_NAME...${NC}"
docker run -d \
    --name $CONTAINER_NAME \
    \
    `# === Environment Variables ===` \
    -e ASPNETCORE_ENVIRONMENT=Production \
    -e BACKBLAZE_KEY_ID="$BACKBLAZE_KEY_ID" \
    -e BACKBLAZE_APP_KEY="$BACKBLAZE_APP_KEY" \
    -e BACKBLAZE_BUCKET="$BACKBLAZE_BUCKET" \
    \
    `# === .NET Runtime Disk Safety (CRITICAL) ===` \
    -e TMPDIR=/app/tmp \
    -e TEMP=/app/tmp \
    -e TMP=/app/tmp \
    -e DOTNET_BUNDLE_EXTRACT_BASE_DIR=/app/tmp/bundle \
    -e HOME=/app \
    \
    `# === .NET Runtime Performance ===` \
    -e DOTNET_gcServer=1 \
    -e DOTNET_EnableDiagnostics=0 \
    \
    `# === Volume Mounts (External Storage - Prevents Root FS Fill) ===` \
    -v "${EXTERNAL_STORAGE}:/mnt/torrents" \
    -v "${DOCKER_DATA_DIR}/tmp:/app/tmp" \
    -v "${DOCKER_DATA_DIR}/logs:/app/logs" \
    -v "${DOCKER_DATA_DIR}/data:/app/data" \
    \
    `# === Resource Limits ===` \
    --memory="$MEMORY_LIMIT" \
    --memory-reservation="$MEMORY_RESERVATION" \
    \
    `# === Log Rotation ===` \
    --log-driver json-file \
    --log-opt max-size="$LOG_MAX_SIZE" \
    --log-opt max-file="$LOG_MAX_FILE" \
    \
    `# === Health Check ===` \
    --health-cmd="pgrep -f dotnet || exit 1" \
    --health-interval=30s \
    --health-timeout=10s \
    --health-retries=3 \
    --health-start-period=60s \
    \
    `# === Restart Policy ===` \
    --restart unless-stopped \
    \
    $IMAGE_NAME

# ============================================
# POST-START VERIFICATION
# ============================================
echo ""
echo -e "${GREEN}=== Container $CONTAINER_NAME started successfully! ===${NC}"
echo ""
echo -e "${GREEN}Useful commands:${NC}"
echo -e "  View logs:       docker logs -f $CONTAINER_NAME"
echo -e "  Stop container:  docker stop $CONTAINER_NAME"
echo -e "  Check health:    docker inspect --format='{{.State.Health.Status}}' $CONTAINER_NAME"
echo -e "  Temp disk usage: du -sh ${DOCKER_DATA_DIR}/tmp"
echo ""
echo -e "${GREEN}Disk usage verification:${NC}"
echo -e "  Root FS:         $(df -h / | tail -1 | awk '{print $3 "/" $2 " (" $5 ")"}')"
echo -e "  External:        $(df -h $EXTERNAL_STORAGE | tail -1 | awk '{print $3 "/" $2 " (" $5 ")"}')"
echo ""
