#!/bin/bash
# Script to run the TorreClou Torrent Worker Docker container
# Usage: ./run-torrent-worker.sh

set -e

# Configuration - Update these values as needed
IMAGE_NAME="torreclou-worker"
CONTAINER_NAME="torrent-worker"
BACKBLAZE_KEY_ID="${BACKBLAZE_KEY_ID:-0036961fa7da1a20000000007}"
BACKBLAZE_APP_KEY="${BACKBLAZE_APP_KEY:-K003rx1yKtR0hARydpAqonh7pP8eJRE}"
BACKBLAZE_BUCKET="${BACKBLAZE_BUCKET:-nasrika}"

# Colors for output
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
RED='\033[0;31m'
NC='\033[0m' # No Color

echo -e "${GREEN}Starting TorreClou Torrent Worker...${NC}"

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
# Note: If you encounter build errors, rebuild with --no-cache flag:
# docker build --no-cache -f TorreClou.Worker/Dockerfile -t $IMAGE_NAME .
if ! docker images | grep -q "^$IMAGE_NAME "; then
    echo -e "${YELLOW}Image $IMAGE_NAME not found. Building it...${NC}"
    docker build -f TorreClou.Worker/Dockerfile -t $IMAGE_NAME .
fi

# Run the container
echo -e "${GREEN}Starting container $CONTAINER_NAME...${NC}"
docker run -d \
    --name $CONTAINER_NAME \
    --cap-add SYS_ADMIN \
    --device /dev/fuse \
    --security-opt apparmor=unconfined \
    -e BACKBLAZE_KEY_ID="$BACKBLAZE_KEY_ID" \
    -e BACKBLAZE_APP_KEY="$BACKBLAZE_APP_KEY" \
    -e BACKBLAZE_BUCKET="$BACKBLAZE_BUCKET" \
    -v /mnt/torrenclou:/mnt/torrents \
    --restart unless-stopped \
    $IMAGE_NAME

echo -e "${GREEN}Container $CONTAINER_NAME started successfully!${NC}"
echo -e "${GREEN}View logs with: docker logs -f $CONTAINER_NAME${NC}"
echo -e "${GREEN}Stop container with: docker stop $CONTAINER_NAME${NC}"

