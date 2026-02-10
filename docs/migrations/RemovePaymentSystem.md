## Migration: RemovePaymentSystem

**Migration Name:** `RemovePaymentSystem`  
**Date:** 2026-01-25  
**Type:** Schema cleanup

### Summary
This migration removes all payment-related tables from the database after the payment system was removed from the codebase.

### Tables to Drop
1. `Invoices` - Payment invoices
2. `Deposits` - Crypto deposits
3. `WalletTransactions` - User wallet transactions  
4. `Vouchers` - Discount vouchers

### Columns to Drop
1. `UserJobs.IsRefunded` - Job refund status (if exists in DB)

### Commands to Run

**Using EF Core CLI:**
```bash
# Create the migration
dotnet ef migrations add RemovePaymentSystem --project TorreClou.Infrastructure --startup-project TorreClou.API

# Review the generated migration file
# Edit if needed to ensure safety

# Apply the migration
dotnet ef database update --project TorreClou.Infrastructure --startup-project TorreClou.API
```

**Manual SQL (if not using EF Core migrations):**
```sql
-- Drop payment tables
DROP TABLE IF EXISTS "Invoices" CASCADE;
DROP TABLE IF EXISTS "Deposits" CASCADE;
DROP TABLE IF EXISTS "WalletTransactions" CASCADE;
DROP TABLE IF EXISTS "Vouchers" CASCADE;

-- Drop IsRefunded column from UserJobs (if it exists)
ALTER TABLE "UserJobs" DROP COLUMN IF EXISTS "IsRefunded";

-- Optional: Drop any payment-related indexes or constraints
-- (EF Core migrations should handle this automatically)
```

### Rollback
⚠️ **WARNING:** This migration is destructive and cannot be easily rolled back. All payment data will be permanently deleted.

If you need to preserve payment data for historical/audit purposes:
1. **Backup the database first**
2. Consider exporting payment data before running migration
3. Keep backups for at least 90 days

### Testing
After migration:
1. Verify all payment tables are removed
2. Verify application starts without errors
3. Test job creation flow: POST /api/torrents/quote → POST /api/torrents/create-job
4. Verify no foreign key constraint errors

### Notes
- The payment entities have already been removed from the codebase
- The database schema should now match the C# entity models
- All references to Invoices in code have been removed
