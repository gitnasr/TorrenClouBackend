# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy ONLY the project files we need (no solution file, no other workers)
COPY TorreClou.Core/TorreClou.Core.csproj TorreClou.Core/
COPY TorreClou.Application/TorreClou.Application.csproj TorreClou.Application/
COPY TorreClou.Infrastructure/TorreClou.Infrastructure.csproj TorreClou.Infrastructure/
COPY TorreClou.GoogleDrive.Worker/TorreClou.GoogleDrive.Worker.csproj TorreClou.GoogleDrive.Worker/

# Restore dependencies for the GoogleDrive Worker project only
WORKDIR /src/TorreClou.GoogleDrive.Worker
RUN dotnet restore TorreClou.GoogleDrive.Worker.csproj
WORKDIR /src

# Copy all source code (excluding solution file via .dockerignore)
COPY TorreClou.Core/ TorreClou.Core/
COPY TorreClou.Application/ TorreClou.Application/
COPY TorreClou.Infrastructure/ TorreClou.Infrastructure/
COPY TorreClou.GoogleDrive.Worker/ TorreClou.GoogleDrive.Worker/

# Build and publish
# Explicitly build only this project, ignoring any solution file that might be present
WORKDIR /src/TorreClou.GoogleDrive.Worker
RUN dotnet build TorreClou.GoogleDrive.Worker.csproj -c Release --no-restore -o /app/build /p:BuildProjectReferences=true /p:SolutionDir=
RUN dotnet publish TorreClou.GoogleDrive.Worker.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false /p:SolutionDir=

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create temp directory for S3 downloads
RUN mkdir -p /tmp/s3-downloads && chmod 755 /tmp/s3-downloads

# Create non-root user
RUN groupadd -r appuser && useradd -r -g appuser appuser

# Copy published output
COPY --from=build /app/publish .

# Explicitly copy appsettings.json from build stage source (ensures it's available)
# dotnet publish should include it, but this guarantees it's present
COPY --from=build /src/TorreClou.GoogleDrive.Worker/appsettings.json ./appsettings.json

# Copy entrypoint script
COPY TorreClou.GoogleDrive.Worker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Set environment variables
ENV ASPNETCORE_ENVIRONMENT=Production

# Change ownership to non-root user
RUN chown -R appuser:appuser /app /tmp/s3-downloads

# Set entry point
ENTRYPOINT ["/entrypoint.sh"]
