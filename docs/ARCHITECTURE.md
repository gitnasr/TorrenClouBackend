# TorreClou Architecture

This document provides an overview of TorreClou's system architecture, design patterns, and technical decisions.

## Table of Contents

- [System Overview](#system-overview)
- [Architecture Layers](#architecture-layers)
- [Component Diagram](#component-diagram)
- [Data Flow](#data-flow)
- [Technologies](#technologies)
- [Design Patterns](#design-patterns)
- [Infrastructure](#infrastructure)

## System Overview

TorreClou is a distributed torrent cloud storage system built with .NET 9.0, following Clean Architecture principles. The system downloads torrents in the background and uploads them to cloud storage providers (Google Drive or AWS S3).

### Key Characteristics

- **Distributed Processing** - Separate workers for downloads and uploads
- **Asynchronous** - Background jobs with Hangfire
- **Real-time Updates** - Redis Streams for progress tracking
- **Scalable** - Horizontal scaling of worker services
- **Observable** - Metrics, logs, and tracing built-in
- **Cloud-Native** - Containerized with Docker

## Architecture Layers

TorreClou follows **Clean Architecture** (Onion Architecture) with clear separation of concerns:

```
┌─────────────────────────────────────────────────────┐
│                  Presentation Layer                 │
│               (TorreClou.API)                       │
│  • Controllers                                      │
│  • Middleware                                       │
│  • API Models                                       │
└─────────────────────┬───────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────┐
│               Application Layer                     │
│          (TorreClou.Application)                    │
│  • Business Logic                                   │
│  • Services                                         │
│  • DTOs                                             │
│  • Interfaces                                       │
└─────────────────────┬───────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────┐
│                 Domain Layer                        │
│              (TorreClou.Core)                       │
│  • Entities                                         │
│  • Value Objects                                    │
│  • Domain Interfaces                                │
│  • Enums                                            │
└─────────────────────┬───────────────────────────────┘
                      │
┌─────────────────────┴───────────────────────────────┐
│             Infrastructure Layer                    │
│          (TorreClou.Infrastructure)                 │
│  • Database (PostgreSQL + EF Core)                  │
│  • External Services (Redis, Storage)               │
│  • Repository Implementations                       │
│  • Migrations                                       │
└─────────────────────────────────────────────────────┘
```

### Layer Descriptions

#### 1. **Domain Layer** (`TorreClou.Core`)

The core business logic with no external dependencies.

**Entities:**
- `User` - User account and profile
- `Job` - Background job tracking
- `StorageProfile` - Cloud storage configuration
- `TorrentFile` - Torrent metadata
- `UploadedFile` - Uploaded file tracking

**Enums:**
- `JobStatus` - Pending, InProgress, Completed, Failed
- `JobType` - TorrentDownload, GoogleDriveUpload, S3Upload
- `StorageProviderType` - GoogleDrive, S3
- `FileStatus` - Pending, Uploading, Completed, Failed

**DTOs:**
- Request/Response models for API
- Data transfer between layers

#### 2. **Application Layer** (`TorreClou.Application`)

Business logic and use cases.

**Services:**
- `ITorrentAnalysisService` - Analyze torrents and extract metadata
- `ITorrentService` - Torrent management
- `IJobService` - Background job orchestration
- `IStorageProfileService` - Manage storage configurations
- `IGoogleDriveService` - Google Drive integration
- `IS3Service` - AWS S3 integration

**Patterns:**
- Service layer for business logic
- Result pattern for error handling
- CQRS-like separation (commands vs queries)

#### 3. **Infrastructure Layer** (`TorreClou.Infrastructure`)

External concerns and data persistence.

**Database:**
- Entity Framework Core 9.0
- PostgreSQL with JSONB support
- Repository pattern
- Unit of Work pattern

**External Services:**
- Redis (caching, job queues, streams)
- Google Drive API
- AWS S3 SDK
- qBittorrent Web API

**Configuration:**
- Shared configurations (Database, Redis, Hangfire, OpenTelemetry)
- Serilog logging

#### 4. **Presentation Layer** (`TorreClou.API`)

RESTful API and HTTP concerns.

**Controllers:**
- `AuthController` - Google OAuth authentication
- `TorrentsController` - Torrent analysis and job creation
- `JobsController` - Job monitoring
- `StorageController` - Google Drive and S3 connections
- `HealthController` - Health checks

**Middleware:**
- Global exception handler
- Authentication/Authorization

## Component Diagram

```
┌────────────────────────────────────────────────────────┐
│                        Client                          │
│                  (Web Frontend / API)                  │
└──────────────────────┬─────────────────────────────────┘
                       │ HTTPS
                       ▼
┌──────────────────────────────────────────────────────┐
│                   TorreClou.API                      │
│  • Controllers                                       │
│  • Authentication (JWT + Google OAuth)               │
│  • OpenAPI Documentation                             │
│  • Health Checks                                     │
└──────────┬────────────────────────┬──────────────────┘
           │                        │
           │                        │
    ┌──────▼──────┐         ┌──────▼──────┐
    │  PostgreSQL │         │    Redis    │
    │             │         │             │
    │ • Users     │         │ • Cache     │
    │ • Jobs      │         │ • Streams   │
    │ • Files     │         │ • Locks     │
    └─────────────┘         └──────┬──────┘
                                   │
                                   │ Hangfire Jobs
                    ┌──────────────┼──────────────┐
                    │              │              │
             ┌──────▼─────┐ ┌─────▼──────┐ ┌─────▼──────┐
             │  Torrent   │ │   Google   │ │     S3     │
             │   Worker   │ │Drive Worker│ │   Worker   │
             │            │ │            │ │            │
             │ • Download │ │ • Upload   │ │ • Upload   │
             │ • qBittor. │ │ • OAuth    │ │ • Multipart│
             └──────┬─────┘ └─────┬──────┘ └─────┬──────┘
                    │              │              │
             ┌──────▼──────────────▼──────────────▼─────┐
             │         Cloud Storage Providers          │
             │  • Google Drive                          │
             │  • AWS S3                                │
             └──────────────────────────────────────────┘

┌──────────────────────────────────────────────────────┐
│              Observability Stack                     │
│                                                      │
│  Grafana  ◄──  Prometheus  ◄── Metrics (/metrics)   │
│     ▲                                                │
│     │                                                │
│     └───────  Loki  ◄────────── Logs (Serilog)      │
└──────────────────────────────────────────────────────┘
```

## Data Flow

### 1. Torrent Analysis Flow

```
User → Upload .torrent file
  ↓
API validates file
  ↓
TorrentAnalysisService extracts metadata
  ↓
Return file list, total size, hash info
```

### 2. Download & Upload Flow

```
User → Submit download job
  ↓
API creates Job entity (Status: Pending)
  ↓
Hangfire enqueues job
  ↓
Torrent Worker picks up job
  ├─ Status: InProgress
  ├─ Download via qBittorrent
  ├─ Stream progress to Redis
  └─ Status: Completed (download done)
  ↓
Upload Worker picks up job
  ├─ Status: InProgress
  ├─ Upload to Google Drive or S3
  ├─ Stream progress to Redis
  └─ Status: Completed (upload done)
  ↓
Cleanup temporary files
```

### 3. Real-time Progress Updates

```
Worker → Publishes progress to Redis Stream
  ↓
Frontend → Subscribes to Redis Stream (key: job:progress:{jobId})
  ↓
Receives updates: { downloadedBytes, totalBytes, speed, eta }
```

## Technologies

### Backend

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 9.0 | Application framework |
| ASP.NET Core | 9.0 | Web API framework |
| Entity Framework Core | 9.0 | ORM for database |
| PostgreSQL | 17 | Primary database |
| Redis | 7 | Caching, job queue, streams |
| Hangfire | Latest | Background job processing |
| Serilog | Latest | Structured logging |
| OpenTelemetry | Latest | Observability (metrics, traces) |

### External Services

| Service | Purpose |
|---------|---------|
| Google OAuth 2.0 | User authentication |
| Google Drive API | Cloud storage upload |
| AWS S3 | Alternative cloud storage |
| qBittorrent | Torrent download engine |

### Infrastructure

| Technology | Purpose |
|------------|---------|
| Docker | Containerization |
| Docker Compose | Multi-container orchestration |
| Grafana | Monitoring dashboards |
| Loki | Log aggregation |
| Prometheus | Metrics collection |

## Design Patterns

### 1. **Repository Pattern**

Abstracts data access logic.

```csharp
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<List<T>> GetAllAsync();
    Task AddAsync(T entity);
    Task UpdateAsync(T entity);
    Task DeleteAsync(T entity);
}
```

### 2. **Unit of Work Pattern**

Manages transactions across multiple repositories.

```csharp
public interface IUnitOfWork
{
    IGenericRepository<User> Users { get; }
    IGenericRepository<Job> Jobs { get; }
    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}
```

### 3. **Result Pattern**

Type-safe error handling without exceptions.

```csharp
public class Result<T>
{
    public bool IsSuccess { get; set; }
    public T? Data { get; set; }
    public string? ErrorMessage { get; set; }

    public static Result<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public static Result<T> Failure(string error) => new() { IsSuccess = false, ErrorMessage = error };
}
```

### 4. **Dependency Injection**

Constructor injection throughout the application.

```csharp
public class TorrentAnalysisService : ITorrentAnalysisService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<TorrentAnalysisService> _logger;

    public TorrentAnalysisService(IUnitOfWork unitOfWork, ILogger<TorrentAnalysisService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }
}
```

### 5. **Factory Pattern**

For creating instances based on storage provider type.

```csharp
public interface IS3ResumableUploadServiceFactory
{
    IS3ResumableUploadService Create(StorageProfile profile);
}
```

## Infrastructure

### Database Schema

**Users Table:**
- Id, Email, Name, PictureUrl, GoogleId
- CreatedAt, UpdatedAt

**Jobs Table:**
- Id, UserId, JobType, Status
- TorrentMetadata (JSONB), ProgressData (JSONB)
- CreatedAt, UpdatedAt, CompletedAt

**StorageProfiles Table:**
- Id, UserId, ProviderType
- GoogleDriveAccessToken (encrypted)
- S3AccessKey, S3SecretKey, S3BucketName, S3Region
- IsDefault, CreatedAt

**TorrentFiles Table:**
- Id, JobId, InfoHash, TorrentName
- TotalSize, FileCount
- CreatedAt

**UploadedFiles Table:**
- Id, JobId, FileName, FilePath
- CloudFileId, CloudUrl
- Status, UploadedAt

### Redis Data Structures

**Caching:**
- Key: `cache:{key}`
- TTL: Configurable per cache entry

**Job Queues** (Hangfire):
- `queue:torrents` - Torrent download jobs
- `queue:googledrive` - Google Drive upload jobs
- `queue:s3` - S3 upload jobs

**Streams:**
- `job:progress:{jobId}` - Real-time job progress updates

**Distributed Locks:**
- `lock:job:{jobId}` - Prevent concurrent job processing

### File Storage

**Local Storage:**
- `/mnt/torrents/{jobId}/` - Downloaded torrent files (temporary)

**Cloud Storage:**
- Google Drive: Uploaded to user's Drive
- AWS S3: Uploaded to configured bucket

## Security

### Authentication

- **JWT Tokens** - Stateless authentication
- **Google OAuth 2.0** - Third-party authentication
- **Token Expiration** - Configurable (default: 24 hours)

### Authorization

- **User-scoped Resources** - Users can only access their own jobs
- **Admin Endpoints** - Protected with basic authentication

### Data Protection

- **Encrypted Credentials** - Storage tokens encrypted at rest
- **HTTPS Only** - Enforce in production
- **CORS** - Configured allowed origins
- **SQL Injection** - Prevented by EF Core parameterization

## Scalability

### Horizontal Scaling

**API:**
- Stateless design allows multiple instances
- Load balancer distributes requests

**Workers:**
- Add more worker instances for higher throughput
- Hangfire coordinates job distribution

**Database:**
- PostgreSQL read replicas for scaling reads
- Connection pooling

**Redis:**
- Redis Cluster for high availability

### Performance Optimizations

- **Caching** - Reduce database queries
- **Async/Await** - Non-blocking I/O
- **Connection Pooling** - Reuse database connections
- **JSONB Columns** - Flexible schema without joins

## Monitoring

### Metrics (Prometheus)

- HTTP request duration
- Job queue size
- Download/upload speeds
- Database connection pool
- Redis operations

### Logs (Loki)

- Structured logging with Serilog
- Log levels: Debug, Information, Warning, Error, Fatal
- Correlation IDs for request tracking

### Dashboards (Grafana)

- System health overview
- Job processing metrics
- API performance
- Resource utilization

## Future Enhancements

- **Magnet Link Support** - Download from magnet URIs
- **Selective Downloads** - Choose specific files from torrent
- **Multi-Tenancy** - Support multiple isolated users
- **Rate Limiting** - Prevent API abuse
- **Webhooks** - Notify external systems on job completion
- **OneDrive/Dropbox** - Additional storage providers

---

**Document Version:** 1.0
**Last Updated:** 2026-01-31
