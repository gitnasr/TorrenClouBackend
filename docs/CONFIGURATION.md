# Configuration Reference

Complete reference for all TorreClou configuration options.

## Table of Contents

- [Environment Variables](#environment-variables)
- [Application Settings](#application-settings)
- [Database Configuration](#database-configuration)
- [Redis Configuration](#redis-configuration)
- [JWT Configuration](#jwt-configuration)
- [Google OAuth Configuration](#google-oauth-configuration)
- [Google Drive Configuration](#google-drive-configuration)
- [AWS S3 Configuration](#aws-s3-configuration)
- [Worker Configuration](#worker-configuration)
- [Observability Configuration](#observability-configuration)
- [SMTP Configuration](#smtp-configuration)

## Environment Variables

All sensitive configuration is done through environment variables defined in `.env` file.

### Quick Reference

| Variable | Required | Default | Description |
|----------|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | No | Production | Application environment (Development, Production) |
| `POSTGRES_DB` | Yes | - | PostgreSQL database name |
| `POSTGRES_USER` | Yes | - | PostgreSQL username |
| `POSTGRES_PASSWORD` | Yes | - | PostgreSQL password |
| `POSTGRES_PORT` | No | 5432 | PostgreSQL port |
| `REDIS_PORT` | No | 6379 | Redis port |
| `API_PORT` | No | 5000 | API external port |
| `ALLOWED_ORIGINS` | Yes | - | CORS allowed origins (comma-separated) |
| `JWT_SECRET` | Yes | - | JWT signing key (min 32 chars) |
| `JWT_ISSUER` | No | TorrenClo | JWT token issuer |
| `JWT_AUDIENCE` | No | TorrenCloAPI | JWT token audience |
| `JWT_EXPIRATION_HOURS` | No | 24 | JWT token validity (hours) |
| `MAX_CONCURRENT_JOBS` | No | 3 | Max concurrent torrent downloads |
| `MAX_CONCURRENT_UPLOADS` | No | 2 | Max concurrent cloud uploads |
| `GOOGLE_CLIENT_ID` | Yes | - | Google OAuth client ID (user login) |
| `GOOGLE_DRIVE_CLIENT_ID` | Yes | - | Google Drive OAuth client ID |
| `GOOGLE_DRIVE_CLIENT_SECRET` | Yes | - | Google Drive OAuth client secret |
| `GOOGLE_DRIVE_REDIRECT_URI` | Yes | - | Google Drive OAuth callback URL |
| `FRONTEND_URL` | Yes | - | Frontend application URL |
| `GRAFANA_ADMIN_USER` | No | admin | Grafana admin username |
| `GRAFANA_ADMIN_PASSWORD` | Yes | - | Grafana admin password |
| `SMTP_HOST` | No | - | SMTP server host (optional) |
| `SMTP_PORT` | No | 587 | SMTP server port |
| `SMTP_USERNAME` | No | - | SMTP username (optional) |
| `SMTP_PASSWORD` | No | - | SMTP password (optional) |
| `SMTP_FROM` | No | - | Email sender address (optional) |

## Application Settings

### ASP.NET Core Environment

```env
ASPNETCORE_ENVIRONMENT=Production
```

**Values:**
- `Development` - Enable detailed errors, Swagger, no HTTPS redirect
- `Production` - Minimal errors, HTTPS redirect, HSTS

**Recommendations:**
- Use `Development` for local development
- Use `Production` for deployed environments

## Database Configuration

### PostgreSQL

```env
POSTGRES_DB=torrenclo
POSTGRES_USER=torrenclo_user
POSTGRES_PASSWORD=your_secure_password_here
POSTGRES_PORT=5432
```

**Connection String Format:**
```
Host=postgres;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD};Port=${POSTGRES_PORT}
```

**Recommendations:**
- Use a strong password (16+ characters, mixed case, numbers, symbols)
- Change default credentials in production
- Enable SSL for production databases

**Database Features Used:**
- JSONB columns for flexible metadata storage
- Full-text search
- Connection pooling (handled by EF Core)

### Migrations

Migrations are applied **automatically** on API startup. To manage manually:

```bash
# Create migration
dotnet ef migrations add MigrationName --project TorreClou.Infrastructure --startup-project TorreClou.API

# Apply migrations
dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API

# Rollback migration
dotnet ef database update PreviousMigrationName --project TorreClou.Infrastructure --startup-project TorreClou.API
```

## Redis Configuration

```env
REDIS_PORT=6379
```

**Connection String:**
```
redis:${REDIS_PORT}
```

**Redis Usage:**
- **Caching** - Reduce database load
- **Hangfire Storage** - Job queue and scheduling
- **Distributed Locks** - Prevent duplicate job processing
- **Streams** - Real-time progress updates

**Recommendations:**
- Enable persistence (RDB or AOF) in production
- Configure memory limits
- Use Redis Cluster for high availability

## JWT Configuration

```env
JWT_SECRET=your_jwt_secret_key_min_32_characters_long
JWT_ISSUER=TorrenClo
JWT_AUDIENCE=TorrenCloAPI
JWT_EXPIRATION_HOURS=24
```

### JWT_SECRET

**Requirements:**
- Minimum 32 characters
- Use cryptographically secure random string
- Never commit to version control

**Generate secure secret:**
```bash
# Linux/macOS
openssl rand -base64 32

# PowerShell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

### JWT_ISSUER / JWT_AUDIENCE

- Identifies your application
- Must match on token generation and validation

### JWT_EXPIRATION_HOURS

- Token validity period
- Shorter = more secure, but more frequent logins
- Longer = better UX, but higher risk if token leaked

**Recommendations:**
- Development: 168 hours (1 week)
- Production: 24 hours (1 day)

## Google OAuth Configuration

### User Authentication

```env
GOOGLE_CLIENT_ID=123456789-abcdefg.apps.googleusercontent.com
```

**Setup Guide:** [docs/GOOGLE_DRIVE_SETUP.md](GOOGLE_DRIVE_SETUP.md)

**Scopes Requested:**
- `openid` - User identity
- `email` - Email address
- `profile` - Name and picture

## Google Drive Configuration

### OAuth Credentials

```env
GOOGLE_DRIVE_CLIENT_ID=123456789-abcdefg.apps.googleusercontent.com
GOOGLE_DRIVE_CLIENT_SECRET=GOCSPX-xxxxxxxxxxxxx
GOOGLE_DRIVE_REDIRECT_URI=http://localhost:5000/api/storage/gdrive/callback
FRONTEND_URL=http://localhost:3000
```

**Important Notes:**
- Can use **same credentials** as user authentication
- `GOOGLE_DRIVE_REDIRECT_URI` must match exactly what's in Google Cloud Console
- For production, use HTTPS URLs

**Scopes Requested:**
- `https://www.googleapis.com/auth/drive.file` - Create and access app's own files

### Production Example

```env
GOOGLE_DRIVE_REDIRECT_URI=https://api.yourdomain.com/api/storage/gdrive/callback
FRONTEND_URL=https://yourdomain.com
```

## AWS S3 Configuration

S3 credentials are configured **per-user** through the API (not environment variables).

**Storage Profile Example:**
```json
{
  "providerType": "S3",
  "s3AccessKey": "AKIAIOSFODNN7EXAMPLE",
  "s3SecretKey": "wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
  "s3BucketName": "my-torrenclo-bucket",
  "s3Region": "us-east-1"
}
```

**Supported Regions:**
- `us-east-1`, `us-west-2`, `eu-west-1`, etc.
- See [AWS Regions](https://docs.aws.amazon.com/general/latest/gr/rande.html)

**Required IAM Permissions:**
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:PutObject",
        "s3:GetObject",
        "s3:DeleteObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::my-torrenclo-bucket",
        "arn:aws:s3:::my-torrenclo-bucket/*"
      ]
    }
  ]
}
```

## Worker Configuration

```env
MAX_CONCURRENT_JOBS=3
MAX_CONCURRENT_UPLOADS=2
```

### MAX_CONCURRENT_JOBS

Maximum number of torrents to download simultaneously.

**Considerations:**
- Higher = faster overall, but more CPU/disk I/O
- Depends on network speed and disk performance
- Recommended: 2-5 for typical servers

### MAX_CONCURRENT_UPLOADS

Maximum number of files to upload to cloud simultaneously.

**Considerations:**
- Google Drive has rate limits (10,000 requests per 100 seconds)
- S3 can handle higher concurrency
- Recommended: 2-4 for Google Drive, 5-10 for S3

### Torrent Download Path

Workers store downloaded torrents temporarily:

```env
TORRENT_DOWNLOAD_PATH=/mnt/torrents
```

**Default:** `/mnt/torrents`

**Recommendations:**
- Use fast storage (SSD preferred)
- Ensure sufficient space (100GB+ recommended)
- Mount separate volume for isolation

### Worker Scaling

To scale workers, add more instances in `docker-compose.yml`:

```yaml
torrent-worker-2:
  image: torrenclo/worker:latest
  environment:
    - MAX_CONCURRENT_JOBS=3
  # ... same config as torrent-worker
```

## Observability Configuration

### Grafana

```env
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=changeme_please
```

**Access:** http://localhost:3001

**Recommendations:**
- Change default password immediately
- Use strong password in production
- Configure SMTP for alerts (optional)

### Prometheus

**Scrape Configuration:** `observability/prometheus.yml`

```yaml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: 'torrenclo-api'
    static_configs:
      - targets: ['api:8080']
```

**Metrics Endpoint:** `http://localhost:5000/metrics`

### Loki

**Configuration:** Automatic via Serilog

**Default URL:** `http://loki:3100`

**Cloud Loki (Optional):**
```env
OBSERVABILITY__LOKIURL=https://logs-prod-us-central1.grafana.net
OBSERVABILITY__LOKIUSERNAME=123456
OBSERVABILITY__LOKIAPIKEY=your_grafana_cloud_api_key
```

### OpenTelemetry (Optional)

For cloud tracing (Grafana Cloud, Honeycomb, etc.):

```env
OBSERVABILITY__ENABLETRACING=true
OBSERVABILITY__OTLPENDPOINT=https://otlp.grafana.net/otlp
OBSERVABILITY__OTLPHEADERS=Authorization=Basic%20base64encodedtoken
```

## SMTP Configuration

Optional email notifications.

```env
SMTP_HOST=smtp.gmail.com
SMTP_PORT=587
SMTP_USERNAME=your_email@gmail.com
SMTP_PASSWORD=your_app_password
SMTP_FROM=noreply@yourdomain.com
```

**Use Cases:**
- Job completion notifications
- Error alerts
- User registration confirmation

**Gmail App Password:**
1. Enable 2FA on your Google account
2. Go to [App Passwords](https://myaccount.google.com/apppasswords)
3. Generate new app password
4. Use generated password in `SMTP_PASSWORD`

## Docker Compose Overrides

Create `docker-compose.override.yml` for local customizations:

```yaml
version: '3.8'

services:
  api:
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
    ports:
      - "5001:8080"  # Custom port

  postgres:
    ports:
      - "5433:5432"  # Avoid conflict with local PostgreSQL
```

This file is ignored by Git and won't affect production.

## Health Checks

### Endpoints

| Endpoint | Purpose | Cached |
|----------|---------|--------|
| `/api/health` | Liveness probe | No (always fast) |
| `/api/health/ready` | Readiness probe | Yes (10s cache) |
| `/api/health/detailed` | Debugging | No (expensive) |

### Docker Healthcheck

```yaml
healthcheck:
  test: ["CMD-SHELL", "curl -f http://localhost:8080/api/health || exit 1"]
  interval: 30s
  timeout: 10s
  retries: 3
  start_period: 40s
```

## Security Best Practices

1. **Never commit `.env`** - Add to `.gitignore`
2. **Use strong passwords** - 16+ characters, random
3. **Rotate secrets** - Periodically update JWT secret, passwords
4. **Enable HTTPS** - Use reverse proxy (Nginx, Traefik) in production
5. **Limit CORS origins** - Don't use `*` in production
6. **Update dependencies** - Run `dotnet outdated` regularly
7. **Monitor logs** - Check for suspicious activity

## Performance Tuning

### Database

- **Connection Pool Size:** Default 100 (EF Core)
- **Command Timeout:** 30 seconds
- **Max Connections:** Configure in PostgreSQL (`max_connections`)

### Redis

- **Max Memory:** Configure `maxmemory` in Redis
- **Eviction Policy:** `allkeys-lru` recommended
- **Persistence:** Enable RDB snapshots in production

### API

- **Kestrel Limits:**
  ```json
  {
    "Kestrel": {
      "Limits": {
        "MaxConcurrentConnections": 100,
        "MaxRequestBodySize": 104857600
      }
    }
  }
  ```

## Troubleshooting

### Common Configuration Issues

**Problem:** API can't connect to PostgreSQL

**Solution:** Check `ConnectionStrings:DefaultConnection` in `appsettings.json` matches environment variables

---

**Problem:** CORS errors in browser

**Solution:** Add frontend URL to `ALLOWED_ORIGINS` in `.env`

---

**Problem:** Google OAuth redirect mismatch

**Solution:** Ensure `GOOGLE_DRIVE_REDIRECT_URI` matches exactly what's in Google Cloud Console

---

**Problem:** Workers not processing jobs

**Solution:** Verify Redis connection, check Hangfire dashboard for errors

## Configuration File Locations

| File | Purpose |
|------|---------|
| `.env` | Environment variables (not committed) |
| `appsettings.json` | Default application settings |
| `appsettings.Development.json` | Development overrides |
| `appsettings.Production.json` | Production overrides |
| `docker-compose.yml` | Service definitions |
| `docker-compose.override.yml` | Local overrides (not committed) |

---

**Document Version:** 1.0
**Last Updated:** 2026-01-31
