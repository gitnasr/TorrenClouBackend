# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files for dependency restoration
COPY TorreClou.sln .
COPY TorreClou.Core/TorreClou.Core.csproj TorreClou.Core/
COPY TorreClou.Application/TorreClou.Application.csproj TorreClou.Application/
COPY TorreClou.Infrastructure/TorreClou.Infrastructure.csproj TorreClou.Infrastructure/
COPY TorreClou.API/TorreClou.API.csproj TorreClou.API/
COPY TorreClou.Worker/TorreClou.Worker.csproj TorreClou.Worker/
COPY TorreClou.GoogleDrive.Worker/TorreClou.GoogleDrive.Worker.csproj TorreClou.GoogleDrive.Worker/

# Restore dependencies
RUN dotnet restore TorreClou.sln

# Copy all source code
COPY TorreClou.Core/ TorreClou.Core/
COPY TorreClou.Application/ TorreClou.Application/
COPY TorreClou.Infrastructure/ TorreClou.Infrastructure/
COPY TorreClou.GoogleDrive.Worker/ TorreClou.GoogleDrive.Worker/

# Build and publish
WORKDIR /src/TorreClou.GoogleDrive.Worker
RUN dotnet build -c Release --no-restore -o /app/build
RUN dotnet publish -c Release --no-restore -o /app/publish /p:UseAppHost=false

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install rclone and fuse for Backblaze B2 mount (to read uploaded files)
RUN apt-get update && apt-get install -y --no-install-recommends \
    fuse3 curl unzip ca-certificates \
    && curl -O https://downloads.rclone.org/rclone-current-linux-amd64.zip \
    && unzip rclone-current-linux-amd64.zip \
    && cp rclone-*-linux-amd64/rclone /usr/bin/ \
    && chmod 755 /usr/bin/rclone \
    && rm -rf rclone-* \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

# Create mount point for Backblaze B2
RUN mkdir -p /mnt/backblaze

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published output
COPY --from=build /app/publish .

# Copy entrypoint script
COPY TorreClou.GoogleDrive.Worker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production

# Change ownership to non-root user
RUN chown -R appuser:appuser /app /mnt/backblaze

# Note: FUSE mounts require special Docker privileges
# Container must be run with: --cap-add SYS_ADMIN --device /dev/fuse
# Or with: --privileged (less secure)
# The entrypoint script will check for FUSE availability

# Set entry point
ENTRYPOINT ["/entrypoint.sh"]
