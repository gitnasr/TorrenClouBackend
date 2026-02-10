# Database Migration: SimplifyAuthentication

**Migration Name:** `SimplifyAuthentication`  
**Date:** 2026-01-25  
**Type:** Schema simplification

## Summary
This migration removes OAuth authentication fields and user roles from the database, simplifying to a single-user self-hosted deployment model.

## Tables to Modify

### Users Table

#### Columns to DROP
```sql
ALTER TABLE "Users" DROP COLUMN IF EXISTS "OAuthProvider";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "OAuthSubjectId";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "Role";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "PhoneNumber";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "IsPhoneNumberVerified";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "Region";
```

#### Columns to KEEP
- `Id`
- `Email`
- `FullName`
- `GoogleDriveEmail` (for Drive storage integration)
- `GoogleDriveRefreshToken` (for Drive storage integration)
- `GoogleDriveTokenCreatedAt` (for Drive storage integration)
- `IsGoogleDriveConnected` (for Drive storage integration)
- `CreatedAt`
- `UpdatedAt`

## Migration Commands

### Using EF Core CLI
```bash
# Create the migration
dotnet ef migrations add SimplifyAuthentication --project TorreClou.Infrastructure --startup-project TorreClou.API

# Review the generated migration file
# Make sure it only drops the OAuth/Role columns

# Apply the migration
dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API
```

### Manual SQL (PostgreSQL)
```sql
-- Drop OAuth and role-related columns
ALTER TABLE "Users" DROP COLUMN IF EXISTS "OAuthProvider";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "OAuthSubjectId";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "Role";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "PhoneNumber";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "IsPhoneNumberVerified";
ALTER TABLE "Users" DROP COLUMN IF EXISTS "Region";

-- Optional: Clear existing users (single admin user will be authenticated via env vars)
-- TRUNCATE TABLE "Users" CASCADE;
```

## Post-Migration Setup

### 1. Configure Admin Credentials
Add to `.env` file:
```bash
ADMIN_EMAIL=admin@localhost.com
ADMIN_PASSWORD=your-secure-password
ADMIN_NAME=Admin User
```

### 2. Verify Authentication
```bash
# Test login
curl -X POST http://localhost:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "admin@localhost.com",
    "password": "your-secure-password"
  }'

# Should return JWT token
```

## Rollback

⚠️ **WARNING:** This migration is destructive. OAuth data will be permanently lost.

To preserve data before migration:
```sql
-- Export OAuth users
COPY (SELECT * FROM "Users") TO '/tmp/users_backup.csv' WITH CSV HEADER;
```

If you need to rollback:
```sql
-- Re-add columns (data will be lost)
ALTER TABLE "Users" ADD COLUMN "OAuthProvider" TEXT;
ALTER TABLE "Users" ADD COLUMN "OAuthSubjectId" TEXT;
ALTER TABLE "Users" ADD COLUMN "Role" INT DEFAULT 0;
ALTER TABLE "Users" ADD COLUMN "PhoneNumber" TEXT DEFAULT '';
ALTER TABLE "Users" ADD COLUMN "IsPhoneNumberVerified" BOOLEAN DEFAULT FALSE;
ALTER TABLE "Users" ADD COLUMN "Region" INT DEFAULT 0;
```

## Testing Checklist

After migration:
- [ ] Application starts without errors
- [ ] Login with env credentials works: `POST /api/auth/login`
- [ ] JWT token is valid and contains userId and email claims
- [ ] Protected endpoints work with JWT token
- [ ] No role-based authorization errors
- [ ] Google Drive storage OAuth still works (separate from login)
- [ ] S3 storage configuration works

## Notes

- **No user table required:** Authentication is stateless via environment variables
- **Google Drive integration:** Still uses OAuth but only for storage, not login
- **Single user:** Only admin credentials from .env can access the system
- **No registration:** Users cannot self-register
