# TorreClou - Open Source Torrent Cloud Storage

![License](https://img.shields.io/badge/license-MIT-blue.svg)
![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![Docker](https://img.shields.io/badge/Docker-ready-2496ED)

**TorreClou** is a self-hosted torrent cloud storage platform that automatically downloads torrents and uploads them to your preferred cloud storage provider (Google Drive or AWS S3).

## Features

- 🚀 **Fast Torrent Downloads** - Powered by qBittorrent for efficient downloading
- ☁️ **Multi-Cloud Support** - Upload to Google Drive or AWS S3
- 🔄 **Background Jobs** - Reliable job processing with Hangfire
- 📊 **Real-time Progress** - Track download/upload progress with Redis Streams
- 🔐 **Google OAuth** - Secure authentication with Google
- 📈 **Built-in Observability** - Grafana, Loki, and Prometheus for monitoring
- 🐳 **One-Command Deploy** - Full Docker Compose setup
- 🔧 **Clean Architecture** - Modular, maintainable codebase
- 🆓 **100% Free & Open Source** - No payment walls, no subscriptions

## Quick Start

### Prerequisites

- [Docker](https://docs.docker.com/get-docker/) and [Docker Compose](https://docs.docker.com/compose/install/)
- Google Cloud project (for OAuth authentication) - [Setup Guide](docs/GOOGLE_DRIVE_SETUP.md)

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/torrenclo.git
   cd torrenclo
   ```

2. **Configure environment variables**
   ```bash
   cp .env.example .env
   ```

   Edit `.env` and set your Google OAuth credentials:
   ```env
   # Required: Google OAuth for user authentication
   GOOGLE_CLIENT_ID=your_google_client_id_here

   # Required: Google Drive OAuth for cloud uploads
   GOOGLE_DRIVE_CLIENT_ID=your_google_drive_client_id_here
   GOOGLE_DRIVE_CLIENT_SECRET=your_google_drive_client_secret_here

   # Update secure passwords
   POSTGRES_PASSWORD=your_secure_password_here
   JWT_SECRET=your_jwt_secret_key_min_32_characters_long
   ```

3. **Start all services**
   ```bash
   docker-compose up -d
   ```

4. **Access the application**
   - **API**: http://localhost:5000
   - **API Documentation**: http://localhost:5000/scalar/v1
   - **Hangfire Dashboard**: http://localhost:5000/hangfire
   - **Grafana**: http://localhost:3001 (admin/changeme_please)
   - **Prometheus**: http://localhost:9090

## How It Works

1. **User uploads a .torrent file** via the API
2. **Torrent is analyzed** - Extract file list, total size, metadata
3. **Background worker downloads** the torrent using qBittorrent
4. **Files are uploaded** to your configured cloud storage (Google Drive or S3)
5. **Real-time updates** are streamed via Redis for progress tracking
6. **Job monitoring** available through Hangfire dashboard

## Architecture

TorreClou follows Clean Architecture principles with clear separation of concerns:

```
TorreClou.Core          - Domain entities, DTOs, interfaces
TorreClou.Application   - Business logic, services
TorreClou.Infrastructure - External concerns (database, Redis, storage)
TorreClou.API           - RESTful API endpoints
TorreClou.Worker        - Background torrent download worker
TorreClou.GoogleDrive.Worker - Google Drive upload worker
TorreClou.S3.Worker     - AWS S3 upload worker (optional)
```

For detailed architecture documentation, see [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Configuration

All configuration is done through environment variables for security and portability.

Key configuration options:
- **Database**: PostgreSQL connection settings
- **Redis**: Cache and job queue
- **JWT**: Authentication tokens
- **Google OAuth**: User authentication
- **Google Drive**: Cloud storage uploads
- **AWS S3**: Alternative cloud storage (optional)
- **Worker Settings**: Concurrent jobs, upload limits

See [docs/CONFIGURATION.md](docs/CONFIGURATION.md) for the complete reference.

## Google Drive Setup

To use Google Drive as your cloud storage:

1. Create a Google Cloud project
2. Enable Google Drive API
3. Configure OAuth consent screen
4. Create OAuth 2.0 credentials
5. Add credentials to `.env` file

**Detailed step-by-step guide**: [docs/GOOGLE_DRIVE_SETUP.md](docs/GOOGLE_DRIVE_SETUP.md)

## Development

### Local Development Setup

1. **Install prerequisites**
   - .NET 9.0 SDK
   - Docker & Docker Compose
   - PostgreSQL (or use Docker)
   - Redis (or use Docker)

2. **Start infrastructure services**
   ```bash
   docker-compose up -d postgres redis
   ```

3. **Run database migrations**
   ```bash
   dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API
   ```

4. **Run the API**
   ```bash
   dotnet run --project TorreClou.API
   ```

5. **Run workers (in separate terminals)**
   ```bash
   dotnet run --project TorreClou.Worker
   dotnet run --project TorreClou.GoogleDrive.Worker
   ```

### Building the Project

```bash
dotnet build
```

### Running Tests

```bash
dotnet test
```

## Observability

TorreClou includes a complete observability stack:

- **Grafana** - Dashboards and visualization (http://localhost:3001)
- **Loki** - Log aggregation
- **Prometheus** - Metrics collection

All services automatically send logs to Loki and expose metrics for Prometheus.

## Database Migrations

Database migrations are applied **automatically** on API startup. When you update to a new version with schema changes, just restart the API container:

```bash
docker-compose restart api
```

For manual migration management:
```bash
# Create a new migration
dotnet ef migrations add MigrationName --project TorreClou.Infrastructure --startup-project TorreClou.API

# Apply migrations manually
dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API
```

## API Documentation

Interactive API documentation is available at:
- **Scalar UI**: http://localhost:5000/scalar/v1 (Development only)
- **OpenAPI Spec**: http://localhost:5000/openapi/v1.json

## Contributing

We welcome contributions! Please see [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## Roadmap

- [ ] Additional cloud storage providers (OneDrive, Dropbox)
- [ ] Web UI for easier management
- [ ] Magnet link support
- [ ] Selective file download
- [ ] Multi-user support with quotas
- [ ] Torrent search integration

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Support

- **Issues**: https://github.com/yourusername/torrenclo/issues
- **Discussions**: https://github.com/yourusername/torrenclo/discussions

## Acknowledgments

- Built with [.NET 9.0](https://dotnet.microsoft.com/)
- Powered by [qBittorrent](https://www.qbittorrent.org/)
- Background jobs by [Hangfire](https://www.hangfire.io/)
- Monitoring with [Grafana](https://grafana.com/)

---

**Made with ❤️ by the TorreClou community**
