# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Project Overview

TorreClou is a .NET 9.0 open-source torrent cloud storage platform with the following key features:
- Torrent file processing and metadata extraction using MonoTorrent
- Multi-cloud upload support (Google Drive, AWS S3)
- Job queue system for background tasks with Hangfire
- Compliance/strike system for user violations
- Google OAuth authentication with JWT tokens
- PostgreSQL database with EF Core
- Redis for caching and real-time job updates
- Observability with OpenTelemetry and Serilog

## Architecture

This solution follows **Clean Architecture** with clear separation of concerns across 5 projects:

### TorreClou.Core
Domain layer containing business entities, interfaces, DTOs, and domain logic. No external dependencies.

**Key patterns:**
- **BaseEntity**: All entities inherit from this with `Id`, `CreatedAt`, `UpdatedAt` fields
- **Result pattern**: Operations return `Result<T>` or `Result` for functional error handling (see `TorreClou.Core.Shared.Result`)
- **Specification pattern**: Query logic encapsulated in specifications (see `ISpecification<T>` and `BaseSpecification<T>`)
- **Custom exceptions**: Domain exceptions inherit from `BaseAppException` with error codes and HTTP status codes

**Structure:**
- `Entities/` - Domain entities organized by domain (Jobs, Torrents, Storage, Compliance)
- `DTOs/` - Data transfer objects organized by domain
- `Interfaces/` - Repository and service interfaces
- `Specifications/` - Query specifications for complex database queries
- `Exceptions/` - Custom exception classes
- `Enums/` - Domain enumerations (UserRole, StorageProviderType, JobStatus, etc.)

### TorreClou.Application
Application/business logic layer. Contains services that orchestrate domain logic and infrastructure.

**Key services:**
- `IAuthService` - Google OAuth and JWT authentication
- `ITorrentService` - Torrent file processing with MonoTorrent
- `ITorrentAnalysisService` - Torrent analysis and file selection
- `IJobService` - Job creation and management
- `IStorageProfilesService` - Storage provider configuration
- `IGoogleDriveAuthService` - Google Drive OAuth and token management
- `ITorrentHealthService` - Torrent health monitoring
- `ITrackerScraper` - UDP tracker scraping for torrent health

**Structure:**
- `Services/` - Business logic services
- `Services/Torrent/` - Torrent-specific services
- `Services/Storage/` - Storage provider services
- `Extensions/` - Service registration extension methods

### TorreClou.Infrastructure
Infrastructure layer implementing interfaces from Core. Handles data persistence and external services.

**Key components:**
- **ApplicationDbContext**: EF Core DbContext with PostgreSQL
- **UnitOfWork pattern**: `IUnitOfWork` provides access to repositories and manages transactions
- **Generic Repository**: `IGenericRepository<T>` for common CRUD operations with specification support
- **UpdateAuditableEntitiesInterceptor**: Automatically sets `UpdatedAt` timestamps
- **TokenService**: JWT token generation and validation
- **RedisStreamService**: Redis Streams for real-time job updates
- **JobStatusService**: Background job status tracking with Hangfire

**Structure:**
- `Data/` - DbContext and UnitOfWork
- `Repositories/` - Repository implementations
- `Services/` - Infrastructure service implementations (Redis, Job tracking, Health checks)
- `Migrations/` - EF Core database migrations
- `Settings/` - Configuration classes
- `Interceptors/` - EF Core interceptors
- `Helpers/` - Utility classes

### TorreClou.API
Web API layer built with ASP.NET Core. Entry point for HTTP requests.

**Features:**
- JWT Bearer authentication
- Global exception handling via `GlobalExceptionHandler` middleware
- OpenAPI/Scalar documentation (dev only)
- OpenTelemetry instrumentation for tracing and metrics
- Serilog structured logging

**Structure:**
- `Controllers/` - API endpoints (Auth, Torrents, Jobs, Storage, Health, Compliance)
- `Middleware/` - Custom middleware (GlobalExceptionHandler)
- `Extensions/` - Service registration extensions

**Startup flow:**
1. `Program.cs` registers services via extension methods:
   - `AddInfrastructureServices()` - Database, repositories, infrastructure
   - `AddApplicationServices()` - Business logic services
   - `AddApiServices()` - API controllers, OpenTelemetry, exception handling
   - `AddIdentityServices()` - JWT authentication

### TorreClou.Worker
Background worker service for torrent download processing using qBittorrent.

### TorreClou.GoogleDrive.Worker
Background worker service for Google Drive upload processing.

### TorreClou.S3.Worker
Background worker service for AWS S3 upload processing with resumable uploads.

## Development Commands

### Build
```powershell
# Build entire solution
dotnet build

# Build specific project
dotnet build TorreClou.API

# Build for Release
dotnet build -c Release
```

### Run
```powershell
# Run API project
dotnet run --project TorreClou.API

# Run with specific environment
$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --project TorreClou.API

# Run Worker service
dotnet run --project TorreClou.Worker
```

### Database Migrations
```powershell
# Add new migration (run from solution root)
dotnet ef migrations add <MigrationName> --project TorreClou.Infrastructure --startup-project TorreClou.API

# Apply migrations to database
dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API

# Rollback to specific migration
dotnet ef database update <MigrationName> --project TorreClou.Infrastructure --startup-project TorreClou.API

# Remove last migration (if not applied)
dotnet ef migrations remove --project TorreClou.Infrastructure --startup-project TorreClou.API

# Generate SQL script for migration
dotnet ef migrations script --project TorreClou.Infrastructure --startup-project TorreClou.API --output migration.sql
```

### Testing
```powershell
# Currently no test projects exist - tests should be added
# When adding tests, use:
# dotnet test
# dotnet test --filter "FullyQualifiedName~TorreClou.Tests.Unit"
```

## Configuration

Configuration is managed via `appsettings.json` and `appsettings.Development.json`:

**Required settings:**
- `ConnectionStrings:DefaultConnection` - PostgreSQL connection string
- `Jwt:Key` - JWT signing key (must be sufficiently long)
- `Jwt:Issuer` - JWT issuer claim
- `Jwt:Audience` - JWT audience claim
- `Google:ClientId` - Google OAuth client ID for login
- `GoogleDrive:ClientId` - Google Drive API OAuth client ID
- `GoogleDrive:ClientSecret` - Google Drive API OAuth client secret
- `GoogleDrive:RedirectUri` - OAuth callback URL
- `Redis:ConnectionString` - Redis connection string for caching and job streams

**Note:** Never commit actual credentials. Use environment variables or User Secrets for development:
```powershell
dotnet user-secrets set "GoogleDrive:ClientSecret" "your-secret" --project TorreClou.API
```

## Important Patterns and Conventions

### Repository Pattern
Use `IUnitOfWork` to access repositories and manage transactions:
```csharp
var user = await unitOfWork.Repository<User>().GetByIdAsync(userId);
unitOfWork.Repository<User>().Update(user);
await unitOfWork.Complete(); // Commits transaction
```

### Specification Pattern
For complex queries, create specifications inheriting from `BaseSpecification<T>`:
```csharp
var spec = new UserTransactionsSpecification(userId);
var transactions = await unitOfWork.Repository<WalletTransaction>().ListAsync(spec);
```

### Result Pattern
Services should return `Result<T>` for operations that can fail:
```csharp
var result = await torrentService.GetTorrentInfoFromTorrentFileAsync(stream);
if (result.IsFailure)
{
    return BadRequest(result.Error.Message);
}
return Ok(result.Value);
```

### Exception Handling
- Throw domain-specific exceptions inheriting from `BaseAppException` for business rule violations
- `GlobalExceptionHandler` automatically converts exceptions to RFC 7807 Problem Details
- Include error codes for client-side error handling

### Dependency Injection
- Register services using extension methods in each layer's `ServiceCollectionExtensions`
- Use appropriate lifetimes: Scoped for most services, Singleton for stateless utilities
- Constructor injection is the standard pattern

## Database Schema

The database uses PostgreSQL with the following key entities:
- **User** - User accounts with Google OAuth, region, and role
- **UserStorageProfile** - Storage provider credentials (Google Drive, AWS S3) with JSONB configuration
- **RequestedFile** - Torrent metadata with unique InfoHash constraint
- **UserJob** - Background job tracking with selected files (JSONB)
- **UserStrike** - Compliance violations
- **S3UploadProgress** - Resumable S3 upload state tracking

### Key Relationships
- User → UserStorageProfiles (One-to-Many)
- User → UserJobs (One-to-Many)
- User → RequestedFiles (One-to-Many)
- User → Strikes (One-to-Many, Cascade delete)
- UserJob → RequestedFile (Many-to-One)
- UserJob → UserStorageProfile (Many-to-One)
- RequestedFile.InfoHash is unique per user

## API Structure

Controllers inherit from `BaseApiController` and follow RESTful conventions:
- Use `[ApiController]` and `[Route]` attributes
- Return appropriate HTTP status codes
- Use DTOs for request/response bodies
- Apply `[Authorize]` attribute for protected endpoints

## Observability

- **Logging**: Serilog with console sink, structured logging enriched with machine name and thread ID
- **Tracing**: OpenTelemetry with ASP.NET Core, HTTP client, and EF Core instrumentation
- **Metrics**: OpenTelemetry metrics for ASP.NET Core and HTTP clients
- Exporters configured for OTLP protocol

## Security Notes

- JWT tokens used for authentication after Google OAuth
- Sensitive configuration in appsettings.json should be moved to user secrets or environment variables
- All entities have automatic audit timestamps via interceptor
- Unique constraints on User.Email and RequestedFile.InfoHash prevent duplicates
