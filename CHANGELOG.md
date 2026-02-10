# Changelog

All notable changes to TorreClou will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-01-31

### Added - Initial Open Source Release
- Torrent download and cloud upload functionality
- Google Drive integration with OAuth 2.0
- AWS S3 integration with resumable uploads
- Hangfire background job processing
- Redis Streams for real-time progress tracking
- PostgreSQL database with Entity Framework Core
- Automatic database migrations on startup
- Health check endpoints (liveness, readiness, detailed)
- OpenTelemetry metrics and tracing
- Local observability stack (Grafana, Loki, Prometheus)
- Docker Compose for one-command deployment
- RESTful API with OpenAPI documentation
- Google OAuth authentication
- Torrent analysis endpoint (`/api/torrents/analyze`)
- Clean Architecture project structure
- Comprehensive logging with Serilog

### Removed - SaaS to Open Source Conversion
- Payment processing system (Coinremitter integration)
- Pricing engine with PPP (Purchasing Power Parity)
- Marketing features (vouchers, flash sales)
- Transaction and wallet management
- Admin analytics
- Backblaze B2 storage provider
- Cloud observability services (replaced with local stack)

### Changed
- All credentials now configured via environment variables
- Google Drive OAuth now requires user's own Google Cloud credentials
- Health checks optimized with caching and timeouts
- Logging configured for both local and cloud Loki
- Database migrations applied automatically on startup
- Endpoint renamed: `/api/torrents/quote` → `/api/torrents/analyze`
- DTO renamed: `QuoteRequestDto` → `AnalyzeTorrentRequestDto`
- Service renamed: `ITorrentQuoteService` → `ITorrentAnalysisService`

### Security
- Removed all hardcoded credentials
- JWT secret now required via environment variable
- Configurable CORS origins
- Secure password storage for database

### Infrastructure
- PostgreSQL 17 for data persistence
- Redis 7 for caching and job queues
- qBittorrent for torrent downloads
- Hangfire for background job management
- Grafana for dashboards and monitoring
- Loki for log aggregation
- Prometheus for metrics collection

## [Unreleased]

### Planned
- Additional cloud storage providers (OneDrive, Dropbox)
- Web UI for easier management
- Magnet link support
- Selective file download within torrents
- Multi-user support with storage quotas
- Torrent search integration
- Rate limiting and API throttling
- Two-factor authentication
- Webhook notifications

---

## Version History

- **1.0.0** (2026-01-31) - Initial open source release
