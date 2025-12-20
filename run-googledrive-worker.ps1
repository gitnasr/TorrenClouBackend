# PowerShell script to run the TorreClou Google Drive Worker Docker container
# Usage: .\run-googledrive-worker.ps1

$ErrorActionPreference = "Stop"

# Configuration - Update these values as needed
$IMAGE_NAME = "torreclou-googledrive-worker"
$CONTAINER_NAME = "googledrive-worker"
$BACKBLAZE_KEY_ID = if ($env:BACKBLAZE_KEY_ID) { $env:BACKBLAZE_KEY_ID } else { "0036961fa7da1a20000000007" }
$BACKBLAZE_APP_KEY = if ($env:BACKBLAZE_APP_KEY) { $env:BACKBLAZE_APP_KEY } else { "K003rx1yKtR0hARydpAqonh7pP8eJRE" }
$BACKBLAZE_BUCKET = if ($env:BACKBLAZE_BUCKET) { $env:BACKBLAZE_BUCKET } else { "nasrika" }

Write-Host "Starting TorreClou Google Drive Worker..." -ForegroundColor Green

# Check if container already exists and is running
$existingContainer = docker ps -a -q -f "name=$CONTAINER_NAME"
if ($existingContainer) {
    $runningContainer = docker ps -q -f "name=$CONTAINER_NAME"
    if ($runningContainer) {
        Write-Host "Container $CONTAINER_NAME is already running. Stopping it first..." -ForegroundColor Yellow
        docker stop $CONTAINER_NAME
    }
    Write-Host "Removing existing container $CONTAINER_NAME..." -ForegroundColor Yellow
    docker rm $CONTAINER_NAME
}

# Check if image exists, if not, build it
$imageExists = docker images -q $IMAGE_NAME
if (-not $imageExists) {
    Write-Host "Image $IMAGE_NAME not found. Building it..." -ForegroundColor Yellow
    docker build -f TorreClou.GoogleDrive.Worker/Dockerfile -t $IMAGE_NAME .
}

# Run the container with FUSE mount capabilities
Write-Host "Starting container $CONTAINER_NAME with FUSE mount support..." -ForegroundColor Green
docker run -d `
    --name $CONTAINER_NAME `
    --cap-add SYS_ADMIN `
    --device /dev/fuse `
    --security-opt apparmor=unconfined `
    -e "BACKBLAZE_KEY_ID=$BACKBLAZE_KEY_ID" `
    -e "BACKBLAZE_APP_KEY=$BACKBLAZE_APP_KEY" `
    -e "BACKBLAZE_BUCKET=$BACKBLAZE_BUCKET" `
    -v /mnt/torrents:/mnt/torrents `
    --restart unless-stopped `
    $IMAGE_NAME

Write-Host "Container $CONTAINER_NAME started successfully!" -ForegroundColor Green
Write-Host "View logs with: docker logs -f $CONTAINER_NAME" -ForegroundColor Cyan
Write-Host "Stop container with: docker stop $CONTAINER_NAME" -ForegroundColor Cyan
Write-Host "Note: If you see FUSE mount errors, ensure Docker has proper permissions." -ForegroundColor Yellow

