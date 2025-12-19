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

# Restore dependencies for the GoogleDrive Worker project
# This will automatically restore all project dependencies
WORKDIR /src/TorreClou.GoogleDrive.Worker
RUN dotnet restore TorreClou.GoogleDrive.Worker.csproj
WORKDIR /src

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

# Create temp directory for S3 downloads
RUN mkdir -p /tmp/s3-downloads && chmod 755 /tmp/s3-downloads

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
RUN chown -R appuser:appuser /app /tmp/s3-downloads

# Set entry point
ENTRYPOINT ["/entrypoint.sh"]
