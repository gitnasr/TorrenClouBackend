# Configuration Reference

Complete reference for all TorreClou configuration options.

> **All configuration is done through environment variables only.**
> There are no `appsettings.json` files. Copy `.env.example` to `.env` and fill in the values.
> Docker Compose maps `.env` values into containers using the ASP.NET Core convention (`Section__Key`).

## Table of Contents

- [Quick Reference](#quick-reference)
- [Environment](#environment)
- [Database](#database)
- [Redis](#redis)
- [API](#api)
- [JWT Authentication](#jwt-authentication)
- [Google OAuth](#google-oauth)
- [Admin Credentials](#admin-credentials)
- [Workers](#workers)
- [Observability](#observability)
- [Grafana Dashboard](#grafana-dashboard)
- [Health Checks](#health-checks)
- [Security Best Practices](#security-best-practices)
- [Troubleshooting](#troubleshooting)

## Quick Reference

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | No | `Production` | Application environment |
| `POSTGRES_DB` | Yes | `torrenclo` | PostgreSQL database name |
| `POSTGRES_USER` | Yes | `torrenclo_user` | PostgreSQL username |
| `POSTGRES_PASSWORD` | **Yes** | - | PostgreSQL password |
| `POSTGRES_PORT` | No | `5432` | PostgreSQL port |
| `REDIS_PORT` | No | `6379` | Redis port |
| `API_PORT` | No | `5000` | API external port |
| `FRONTEND_URL` | Yes | `http://localhost:3000` | Frontend app URL (OAuth redirects) |
| `APPLY_MIGRATIONS` | No | `true` | Auto-apply EF Core migrations on API startup |
| `JWT_SECRET` | **Yes** | - | JWT signing key (min 32 chars) |
| `JWT_ISSUER` | No | `TorrenClou_API` | JWT token issuer |
| `JWT_AUDIENCE` | No | `TorrenClou_Client` | JWT token audience |
| `GOOGLE_CLIENT_ID` | Yes | - | Google OAuth client ID (user login) |
| `ADMIN_EMAIL` | No | `admin@gitnasr.com` | Admin login email |
| `ADMIN_PASSWORD` | **Yes** | - | Admin login password |
| `ADMIN_NAME` | No | `Admin` | Admin display name |
| `HANGFIRE_WORKER_COUNT` | No | `10` | Hangfire concurrent worker threads |
| `TORRENT_DOWNLOAD_PATH` | No | `/app/downloads` | Torrent download directory |
| `OBSERVABILITY_LOKI_URL` | No | `http://loki:3100` | Loki log aggregation URL |
| `OBSERVABILITY_LOKI_USERNAME` | No | - | Loki username (cloud only) |
| `OBSERVABILITY_LOKI_API_KEY` | No | - | Loki API key (cloud only) |
| `OBSERVABILITY_OTLP_ENDPOINT` | No | - | OpenTelemetry OTLP endpoint |
| `OBSERVABILITY_OTLP_HEADERS` | No | - | OTLP auth headers |
| `OBSERVABILITY_ENABLE_PROMETHEUS` | No | `true` | Enable Prometheus metrics |
| `OBSERVABILITY_ENABLE_TRACING` | No | `true` | Enable distributed tracing |
| `GRAFANA_ADMIN_USER` | No | `admin` | Grafana username |
| `GRAFANA_ADMIN_PASSWORD` | No | `changeme_please` | Grafana password |

## Environment

```env
ASPNETCORE_ENVIRONMENT=Production
```

| Value | Behavior |
|-------|----------|
| `Development` | Debug logging, Swagger/Scalar UI, no HTTPS redirect |
| `Production` | Info logging, HTTPS redirect, HSTS |

## Database

### PostgreSQL

```env
POSTGRES_DB=torrenclo
POSTGRES_USER=torrenclo_user
POSTGRES_PASSWORD=your_secure_password_here
POSTGRES_PORT=5432
```

Docker Compose constructs the connection string automatically:
```
Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}
```

**Recommendations:**
- Use a strong password (16+ characters)
- Enable SSL for production databases

### Migrations

Migrations are applied automatically on API startup when `APPLY_MIGRATIONS=true` (default).

```env
APPLY_MIGRATIONS=true
```

To manage manually:
```bash
# Create migration
dotnet ef migrations add MigrationName --project TorreClou.Infrastructure --startup-project TorreClou.API

# Apply migrations
dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API

# Rollback
dotnet ef database update PreviousMigrationName --project TorreClou.Infrastructure --startup-project TorreClou.API
```

> **Note:** When running EF Core commands locally (outside Docker), set the `ConnectionStrings__DefaultConnection` environment variable to point to your database.

## Redis

```env
REDIS_PORT=6379
```

Docker Compose maps this to `Redis__ConnectionString=redis:6379` inside containers.

**Redis is used for:**
- Distributed locks (prevent duplicate job processing)
- Redis Streams (real-time progress updates)
- Caching

## API

```env
API_PORT=5000
FRONTEND_URL=http://localhost:3000
APPLY_MIGRATIONS=true
```

| Variable | Purpose |
|----------|---------|
| `API_PORT` | External port mapped to container port 8080 |
| `FRONTEND_URL` | Where OAuth callbacks redirect after completion |
| `APPLY_MIGRATIONS` | Auto-apply EF Core migrations on startup |

## JWT Authentication

```env
JWT_SECRET=your_jwt_secret_key_min_32_characters_long
JWT_ISSUER=TorrenClou_API
JWT_AUDIENCE=TorrenClou_Client
```

Mapped to containers as `Jwt__Key`, `Jwt__Issuer`, `Jwt__Audience`.

**Generate a secure secret:**
```bash
# Linux/macOS
openssl rand -base64 32

# PowerShell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

Token validity is fixed at 7 days (configured in code).

## Google OAuth

```env
GOOGLE_CLIENT_ID=123456789-abcdefg.apps.googleusercontent.com
```

This is for **user login authentication only** (Google Sign-In).

**Setup:** [docs/GOOGLE_DRIVE_SETUP.md](GOOGLE_DRIVE_SETUP.md)

**Scopes:** `openid`, `email`, `profile`

### Google Drive

Google Drive OAuth credentials are configured **per-user** via the API (not environment variables).
Users provide their own `ClientId`, `ClientSecret`, and `RedirectUri` through:

```
POST /api/storage/gdrive/credentials
POST /api/storage/gdrive/{profileId}/authenticate
```

See [Google Drive Two-Step Auth Migration](migrations/GoogleDriveTwoStepAuth.md) for details.

## Admin Credentials

```env
ADMIN_EMAIL=admin@example.com
ADMIN_PASSWORD=changeme
ADMIN_NAME=Admin
```

Single-user authentication. These credentials are used for the admin login endpoint.

## Workers

```env
HANGFIRE_WORKER_COUNT=10
TORRENT_DOWNLOAD_PATH=/app/downloads
```

| Variable | Purpose | Default |
|----------|---------|---------|
| `HANGFIRE_WORKER_COUNT` | Number of concurrent Hangfire worker threads per service | `10` |
| `TORRENT_DOWNLOAD_PATH` | Base directory for torrent downloads | `/app/downloads` |

**Recommendations:**
- Keep `HANGFIRE_WORKER_COUNT` at 10 or lower to avoid PostgreSQL connection exhaustion
- Use fast storage (SSD) for `TORRENT_DOWNLOAD_PATH`
- Ensure sufficient disk space (100GB+ recommended)

### AWS S3 / Google Drive Storage

Storage provider credentials are configured **per-user** through the API, not via environment variables.

## Observability

### Loki (Log Aggregation)

```env
OBSERVABILITY_LOKI_URL=http://loki:3100
OBSERVABILITY_LOKI_USERNAME=
OBSERVABILITY_LOKI_API_KEY=
```

**Local Loki:** Uses the docker-compose Loki service at `http://loki:3100` (no auth needed).

**Cloud Loki (Grafana Cloud):**
```env
OBSERVABILITY_LOKI_URL=https://logs-prod-us-central1.grafana.net
OBSERVABILITY_LOKI_USERNAME=123456
OBSERVABILITY_LOKI_API_KEY=your_grafana_cloud_api_key
```

### OpenTelemetry (Tracing & Metrics)

```env
OBSERVABILITY_OTLP_ENDPOINT=https://otlp.grafana.net/otlp
OBSERVABILITY_OTLP_HEADERS=Authorization=Basic%20base64token
OBSERVABILITY_ENABLE_PROMETHEUS=true
OBSERVABILITY_ENABLE_TRACING=true
```

| Variable | Purpose |
|----------|---------|
| `OBSERVABILITY_OTLP_ENDPOINT` | OTLP collector endpoint (Grafana Cloud, Honeycomb, etc.) |
| `OBSERVABILITY_OTLP_HEADERS` | Auth headers for OTLP endpoint (URL-encoded) |
| `OBSERVABILITY_ENABLE_PROMETHEUS` | Enable `/metrics` endpoint for Prometheus scraping |
| `OBSERVABILITY_ENABLE_TRACING` | Enable distributed tracing |

**Prometheus metrics endpoint:** `http://localhost:${API_PORT}/metrics`

## Grafana Dashboard

```env
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=changeme_please
```

**Access:** `http://localhost:3001`

Pre-configured with Loki and Prometheus data sources.

## Health Checks

| Endpoint | Purpose | Cached |
|----------|---------|--------|
| `/api/health` | Liveness probe | No |
| `/api/health/ready` | Readiness probe | Yes (10s) |
| `/api/health/detailed` | Full diagnostic | No |

## Security Best Practices

1. **Never commit `.env`** — Add to `.gitignore`
2. **Use strong passwords** — 16+ characters, random
3. **Rotate secrets** — Periodically update `JWT_SECRET` and passwords
4. **Enable HTTPS** — Use reverse proxy (Nginx, Traefik) in production
5. **Update dependencies** — Run `dotnet outdated` regularly
6. **Monitor logs** — Check Grafana/Loki for suspicious activity

## Troubleshooting

**Problem:** API can't connect to PostgreSQL
**Solution:** Verify `POSTGRES_PASSWORD` in `.env` matches what's set in the running PostgreSQL container. If changing the password, you may need to recreate the postgres volume: `docker compose down -v` then `docker compose up -d`.

---

**Problem:** Google OAuth redirect mismatch
**Solution:** Ensure the `redirectUri` passed in `POST /api/storage/gdrive/credentials` matches exactly what's configured in Google Cloud Console.

---

**Problem:** Workers not processing jobs
**Solution:** Verify Redis is running and reachable. Check the Hangfire dashboard at `/hangfire` for job errors.

---

**Problem:** EF Core migrations fail locally
**Solution:** Set the `ConnectionStrings__DefaultConnection` environment variable:
```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Database=torrenclo;Username=torrenclo_user;Password=yourpassword"
dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API
```

---

**Document Version:** 2.0
**Last Updated:** 2026-02-13
