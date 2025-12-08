# WARP.md

This file provides guidance to WARP (warp.dev) when working with code in this repository.

## Project Overview

TorreClou is a .NET 9.0 torrent cloud storage platform with the following key features:
- Torrent file processing and metadata extraction using MonoTorrent
- Payment processing and wallet management
- Multi-region support with region-specific pricing
- Job queue system for background tasks
- Compliance/strike system for user violations
- Google OAuth authentication with JWT tokens
- PostgreSQL database with EF Core
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
- `Entities/` - Domain entities organized by domain (Financals, Jobs, Marketing, Torrents, Compliance)
- `DTOs/` - Data transfer objects organized by domain
- `Interfaces/` - Repository and service interfaces
- `Specifications/` - Query specifications for complex database queries
- `Exceptions/` - Custom exception classes
- `Enums/` - Domain enumerations (UserRole, RegionCode, etc.)

### TorreClou.Application
Application/business logic layer. Contains services that orchestrate domain logic and infrastructure.

**Key services:**
- `IAuthService` - Google OAuth and JWT authentication
- `ITorrentService` - Torrent file processing with MonoTorrent
- `IPaymentBusinessService` - Payment workflow orchestration
- `IWalletService` - Wallet balance and transaction management
- `ITorrentQuoteService` - Pricing calculations for torrent storage
- `IVoucherService` - Voucher/discount code management
- `IPricingEngine` - Regional pricing and flash sale logic
- `ITrackerScraper` - UDP tracker scraping for torrent health

**Structure:**
- `Services/` - Business logic services
- `Services/Torrent/` - Torrent-specific services
- `Services/Billing/` - Billing-specific services
- `Extensions/` - Service registration extension methods

### TorreClou.Infrastructure
Infrastructure layer implementing interfaces from Core. Handles data persistence and external services.

**Key components:**
- **ApplicationDbContext**: EF Core DbContext with PostgreSQL
- **UnitOfWork pattern**: `IUnitOfWork` provides access to repositories and manages transactions
- **Generic Repository**: `IGenericRepository<T>` for common CRUD operations with specification support
- **UpdateAuditableEntitiesInterceptor**: Automatically sets `UpdatedAt` timestamps
- **CoinremitterService**: Cryptocurrency payment gateway integration (TRX)
- **TokenService**: JWT token generation and validation

**Structure:**
- `Data/` - DbContext and UnitOfWork
- `Repositories/` - Repository implementations
- `Services/` - Infrastructure service implementations
- `Migrations/` - EF Core database migrations
- `Settings/` - Configuration classes (CoinremitterSettings, etc.)
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
- `Controllers/` - API endpoints (Auth, Torrents, Payments, Invoice, AdminPayments)
- `Middleware/` - Custom middleware (GlobalExceptionHandler)
- `Extensions/` - Service registration extensions

**Startup flow:**
1. `Program.cs` registers services via extension methods:
   - `AddInfrastructureServices()` - Database, repositories, infrastructure
   - `AddApplicationServices()` - Business logic services
   - `AddApiServices()` - API controllers, OpenTelemetry, exception handling
   - `AddIdentityServices()` - JWT authentication

### TorreClou.Worker
Background worker service for asynchronous job processing (currently minimal implementation).

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
- `Google:ClientId` - Google OAuth client ID
- `Coinremitter:ApiKey` - Coinremitter API key
- `Coinremitter:ApiPassword` - Coinremitter API password
- `Coinremitter:WebhookUrl` - Webhook URL for payment notifications

**Note:** Never commit actual credentials. Use User Secrets for development:
```powershell
dotnet user-secrets set "Jwt:Key" "your-secret-key" --project TorreClou.API
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
- **User** - User accounts with OAuth, region, role, and balance calculation
- **UserStorageProfile** - Storage provider credentials (JSONB column)
- **RequestedFile** - Torrent metadata with unique InfoHash constraint
- **WalletTransaction** - Financial transactions (deposits, payments)
- **UserJob** - Background job tracking with selected file indices (array type)
- **Invoice** - Billing invoices with pricing snapshot (JSONB)
- **Deposit** - Cryptocurrency deposits via Coinremitter
- **Voucher** - Discount/promotion codes with usage tracking
- **UserStrike** - Compliance violations

### Key Relationships
- User → WalletTransactions (One-to-Many, Cascade delete)
- User → UploadedTorrentFiles (One-to-Many)
- User → Strikes (One-to-Many, Cascade delete)
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
