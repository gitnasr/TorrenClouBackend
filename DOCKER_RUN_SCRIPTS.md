# Docker Run Scripts

This directory contains convenient scripts to run the TorreClou worker Docker containers without having to copy-paste long Docker commands.

## Available Scripts

### For Linux/Mac (Bash):
- `run-torrent-worker.sh` - Runs the Torrent Worker container
- `run-googledrive-worker.sh` - Runs the Google Drive Worker container

### For Windows (PowerShell):
- `run-torrent-worker.ps1` - Runs the Torrent Worker container
- `run-googledrive-worker.ps1` - Runs the Google Drive Worker container

## Usage

### Linux/Mac:
```bash
# Make scripts executable (first time only)
chmod +x run-torrent-worker.sh
chmod +x run-googledrive-worker.sh

# Run the workers
./run-torrent-worker.sh
./run-googledrive-worker.sh
```

### Windows:
```powershell
# Run the workers
.\run-torrent-worker.ps1
.\run-googledrive-worker.ps1
```

## Configuration

The scripts use default Backblaze credentials, but you can override them using environment variables:

```bash
# Linux/Mac
export BACKBLAZE_KEY_ID="your-key-id"
export BACKBLAZE_APP_KEY="your-app-key"
export BACKBLAZE_BUCKET="your-bucket-name"
./run-googledrive-worker.sh

# Windows PowerShell
$env:BACKBLAZE_KEY_ID="your-key-id"
$env:BACKBLAZE_APP_KEY="your-app-key"
$env:BACKBLAZE_BUCKET="your-bucket-name"
.\run-googledrive-worker.ps1
```

Or edit the scripts directly to change the default values.

## What the Scripts Do

1. **Check for existing containers**: Stops and removes any existing container with the same name
2. **Build images if needed**: Automatically builds the Docker image if it doesn't exist
3. **Run with proper settings**: Includes all necessary Docker options:
   - `--cap-add SYS_ADMIN` - Required for FUSE mounts
   - `--device /dev/fuse` - Required for FUSE mounts
   - `--security-opt apparmor=unconfined` - Required for FUSE mounts on some systems
   - Environment variables for Backblaze credentials
   - `--restart unless-stopped` - Auto-restart on container failure

## Viewing Logs

After running a script, you can view the container logs:

```bash
# View logs
docker logs -f torrent-worker
docker logs -f googledrive-worker

# Stop containers
docker stop torrent-worker
docker stop googledrive-worker
```

## Troubleshooting

### FUSE Mount Errors
If you see FUSE mount errors, ensure:
- Docker Desktop has proper permissions (on Windows/Mac)
- On Linux, ensure `/dev/fuse` exists and has proper permissions
- Try running with `--privileged` instead of `--cap-add SYS_ADMIN --device /dev/fuse` (less secure)

### Container Already Running
The scripts automatically stop and remove existing containers. If you want to keep an existing container running, stop it manually first:
```bash
docker stop torrent-worker
docker stop googledrive-worker
```

