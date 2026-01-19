# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy ONLY the project files we need (no solution file, no other workers)
COPY TorreClou.Core/TorreClou.Core.csproj TorreClou.Core/
COPY TorreClou.Application/TorreClou.Application.csproj TorreClou.Application/
COPY TorreClou.Infrastructure/TorreClou.Infrastructure.csproj TorreClou.Infrastructure/
COPY TorreClou.Sync.Worker/TorreClou.Sync.Worker.csproj TorreClou.Sync.Worker/

# Restore dependencies for the Sync Worker project only
WORKDIR /src/TorreClou.Sync.Worker
RUN dotnet restore TorreClou.Sync.Worker.csproj
WORKDIR /src

# Copy all source code (excluding solution file via .dockerignore)
COPY TorreClou.Core/ TorreClou.Core/
COPY TorreClou.Application/ TorreClou.Application/
COPY TorreClou.Infrastructure/ TorreClou.Infrastructure/
COPY TorreClou.Sync.Worker/ TorreClou.Sync.Worker/

# Build and publish
# Explicitly build only this project, ignoring any solution file that might be present
WORKDIR /src/TorreClou.Sync.Worker
RUN dotnet build TorreClou.Sync.Worker.csproj -c Release --no-restore -o /app/build /p:BuildProjectReferences=true /p:SolutionDir=
RUN dotnet publish TorreClou.Sync.Worker.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false /p:SolutionDir=

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create mount point for block storage and temp directories
RUN mkdir -p /mnt/torrents /app/tmp /app/tmp/bundle /app/logs /app/data

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published output
COPY --from=build /app/publish .

# Copy entrypoint script
COPY TorreClou.Sync.Worker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# === Environment Variables ===
ENV ASPNETCORE_ENVIRONMENT=Production

# === .NET Runtime Disk Safety (CRITICAL - Prevents root FS fill) ===
ENV TMPDIR=/app/tmp
ENV TEMP=/app/tmp
ENV TMP=/app/tmp
ENV DOTNET_BUNDLE_EXTRACT_BASE_DIR=/app/tmp/bundle
ENV HOME=/app

# === .NET Runtime Performance for long-lived workers ===
ENV DOTNET_gcServer=1
ENV DOTNET_EnableDiagnostics=0

# Change ownership to non-root user
RUN chown -R appuser:appuser /app /mnt/torrents

# Set entry point
ENTRYPOINT ["/entrypoint.sh"]
