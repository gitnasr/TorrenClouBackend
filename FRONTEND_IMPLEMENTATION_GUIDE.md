# Frontend Implementation Guide - Rclone Backend Changes

## Overview
This document outlines the backend changes made in the Rclone branch and provides implementation guidance for the frontend AI Agent to adapt the UI accordingly.

---

## 1. Enhanced Job Status Tracking

### What Changed
The job lifecycle now has **granular intermediate states** for better progress tracking and user experience.

### New Job Statuses

#### Previous States
```typescript
enum JobStatus {
  QUEUED,
  PROCESSING,
  UPLOADING,
  COMPLETED,
  FAILED,
  CANCELLED
}
```

#### New States
```typescript
enum JobStatus {
  // Active Processing States
  QUEUED,              // Job created, waiting to start
  DOWNLOADING,         // Torrent is downloading to block storage
  SYNCING,             // Syncing files from block storage to S3/Backblaze
  PENDING_UPLOAD,      // Ready to upload to Google Drive
  UPLOADING,           // Uploading to Google Drive
  
  // Retry States
  TORRENT_DOWNLOAD_RETRY,  // Retrying torrent download
  UPLOAD_RETRY,            // Retrying Google Drive upload
  SYNC_RETRY,              // Retrying S3 sync
  
  // Terminal States
  COMPLETED,               // Successfully completed
  FAILED,                  // Generic failure
  CANCELLED,               // User cancelled
  TORRENT_FAILED,          // Torrent download failed permanently
  UPLOAD_FAILED,           // Upload failed permanently
  GOOGLE_DRIVE_FAILED      // Google Drive specific failure
}
```

### Frontend Implementation

#### 1. Status Display Component
```typescript
// Example status badge component
const JobStatusBadge = ({ status }: { status: JobStatus }) => {
  const config = {
    QUEUED: { color: 'gray', icon: 'clock', label: 'Queued' },
    DOWNLOADING: { color: 'blue', icon: 'download', label: 'Downloading' },
    SYNCING: { color: 'purple', icon: 'sync', label: 'Syncing to Storage' },
    PENDING_UPLOAD: { color: 'yellow', icon: 'upload-cloud', label: 'Ready to Upload' },
    UPLOADING: { color: 'green', icon: 'upload', label: 'Uploading' },
    TORRENT_DOWNLOAD_RETRY: { color: 'orange', icon: 'refresh', label: 'Retrying Download' },
    UPLOAD_RETRY: { color: 'orange', icon: 'refresh', label: 'Retrying Upload' },
    SYNC_RETRY: { color: 'orange', icon: 'refresh', label: 'Retrying Sync' },
    COMPLETED: { color: 'success', icon: 'check-circle', label: 'Completed' },
    FAILED: { color: 'error', icon: 'x-circle', label: 'Failed' },
    CANCELLED: { color: 'gray', icon: 'ban', label: 'Cancelled' },
    TORRENT_FAILED: { color: 'error', icon: 'alert-triangle', label: 'Download Failed' },
    UPLOAD_FAILED: { color: 'error', icon: 'cloud-off', label: 'Upload Failed' },
    GOOGLE_DRIVE_FAILED: { color: 'error', icon: 'alert-circle', label: 'Google Drive Error' }
  };
  
  const { color, icon, label } = config[status] || config.QUEUED;
  
  return (
    <Badge color={color}>
      <Icon name={icon} /> {label}
    </Badge>
  );
};
```

#### 2. Progress Stepper Component
```typescript
// Multi-step progress indicator
const JobProgressStepper = ({ status }: { status: JobStatus }) => {
  const steps = [
    { key: 'QUEUED', label: 'Queued' },
    { key: 'DOWNLOADING', label: 'Downloading' },
    { key: 'SYNCING', label: 'Syncing' },
    { key: 'UPLOADING', label: 'Uploading' },
    { key: 'COMPLETED', label: 'Completed' }
  ];
  
  const getCurrentStep = (status: JobStatus): number => {
    if (status === 'QUEUED') return 0;
    if (status === 'DOWNLOADING' || status === 'TORRENT_DOWNLOAD_RETRY') return 1;
    if (status === 'SYNCING' || status === 'SYNC_RETRY') return 2;
    if (status === 'PENDING_UPLOAD' || status === 'UPLOADING' || status === 'UPLOAD_RETRY') return 3;
    if (status === 'COMPLETED') return 4;
    return -1; // Failed/Cancelled states
  };
  
  const currentStep = getCurrentStep(status);
  
  return (
    <Stepper activeStep={currentStep}>
      {steps.map((step, index) => (
        <Step key={step.key} completed={currentStep > index}>
          <StepLabel>{step.label}</StepLabel>
        </Step>
      ))}
    </Stepper>
  );
};
```

#### 3. Status Helpers (TypeScript)
```typescript
// Helper functions for status checks
export const isJobActive = (status: JobStatus): boolean => {
  return [
    'QUEUED',
    'DOWNLOADING',
    'SYNCING',
    'PENDING_UPLOAD',
    'UPLOADING',
    'TORRENT_DOWNLOAD_RETRY',
    'UPLOAD_RETRY',
    'SYNC_RETRY'
  ].includes(status);
};

export const isJobRetrying = (status: JobStatus): boolean => {
  return [
    'TORRENT_DOWNLOAD_RETRY',
    'UPLOAD_RETRY',
    'SYNC_RETRY'
  ].includes(status);
};

export const isJobFailed = (status: JobStatus): boolean => {
  return [
    'FAILED',
    'TORRENT_FAILED',
    'UPLOAD_FAILED',
    'GOOGLE_DRIVE_FAILED'
  ].includes(status);
};

export const isJobCompleted = (status: JobStatus): boolean => {
  return status === 'COMPLETED';
};

export const isJobCancelled = (status: JobStatus): boolean => {
  return status === 'CANCELLED';
};
```

---

## 2. Job Statistics Updates

### API Response Changes

#### Previous Response
```typescript
interface JobStatisticsDto {
  totalJobs: number;
  completedJobs: number;
  activeJobs: number;
  failedJobs: number;
}
```

#### New Response
```typescript
interface JobStatisticsDto {
  totalJobs: number;
  
  // Active Jobs Breakdown
  queuedJobs: number;
  downloadingJobs: number;
  syncingJobs: number;
  pendingUploadJobs: number;
  uploadingJobs: number;
  
  // Retry States
  retryingJobs: number;
  
  // Terminal States
  completedJobs: number;
  failedJobs: number;
  cancelledJobs: number;
}
```

### Frontend Implementation

#### Dashboard Statistics Component
```typescript
const JobStatistics = ({ stats }: { stats: JobStatisticsDto }) => {
  return (
    <Grid container spacing={2}>
      <Grid item xs={12} md={6} lg={3}>
        <StatCard
          title="Total Jobs"
          value={stats.totalJobs}
          icon="briefcase"
          color="blue"
        />
      </Grid>
      
      <Grid item xs={12} md={6} lg={3}>
        <StatCard
          title="Active Jobs"
          value={
            stats.queuedJobs +
            stats.downloadingJobs +
            stats.syncingJobs +
            stats.pendingUploadJobs +
            stats.uploadingJobs
          }
          icon="activity"
          color="green"
        />
      </Grid>
      
      <Grid item xs={12} md={6} lg={3}>
        <StatCard
          title="Retrying"
          value={stats.retryingJobs}
          icon="refresh-cw"
          color="orange"
        />
      </Grid>
      
      <Grid item xs={12} md={6} lg={3}>
        <StatCard
          title="Completed"
          value={stats.completedJobs}
          icon="check-circle"
          color="success"
        />
      </Grid>
      
      {/* Detailed Breakdown */}
      <Grid item xs={12}>
        <Card>
          <CardHeader title="Active Jobs Breakdown" />
          <CardContent>
            <List>
              <ListItem>
                <ListItemText primary="Queued" secondary={stats.queuedJobs} />
              </ListItem>
              <ListItem>
                <ListItemText primary="Downloading" secondary={stats.downloadingJobs} />
              </ListItem>
              <ListItem>
                <ListItemText primary="Syncing to Storage" secondary={stats.syncingJobs} />
              </ListItem>
              <ListItem>
                <ListItemText primary="Pending Upload" secondary={stats.pendingUploadJobs} />
              </ListItem>
              <ListItem>
                <ListItemText primary="Uploading" secondary={stats.uploadingJobs} />
              </ListItem>
            </List>
          </CardContent>
        </Card>
      </Grid>
    </Grid>
  );
};
```

---

## 3. Job Creation Flow Changes

### API Changes

#### Request (Unchanged)
```typescript
POST /api/jobs
{
  "userId": number,
  "invoiceId": number
}
```

#### Previous Response
```typescript
{
  "jobId": number,
  "invoiceId": number,
  "storageProfileId": number | null,
  "hasStorageProfileWarning": boolean,
  "storageProfileWarningMessage": string | null
}
```

#### New Response
```typescript
{
  "jobId": number,
  "invoiceId": number,
  "storageProfileId": number | null
}
```

### Breaking Changes
- **Removed**: `hasStorageProfileWarning` and `storageProfileWarningMessage` fields
- **New Behavior**: API returns error if no storage profile exists instead of creating job with warning
- **New Error Code**: `NO_STORAGE` - "You don't have any stored or active Storage Destination"

### Frontend Implementation

#### Job Creation Handler
```typescript
const createJob = async (invoiceId: number) => {
  try {
    const response = await api.post('/api/jobs', { 
      userId: currentUser.id, 
      invoiceId 
    });
    
    if (response.success) {
      showNotification('success', 'Job created successfully!');
      navigateToJobDetails(response.data.jobId);
    }
  } catch (error) {
    if (error.code === 'NO_STORAGE') {
      // Show setup wizard
      showStorageSetupDialog({
        message: error.message,
        onComplete: () => createJob(invoiceId) // Retry after setup
      });
    } else if (error.code === 'JOB_ALREADY_EXISTS') {
      showNotification('warning', 'An active job already exists for this invoice.');
    } else if (error.code === 'JOB_RETRYING') {
      showNotification('info', error.message);
    } else {
      showNotification('error', error.message);
    }
  }
};
```

#### Storage Setup Dialog Component
```typescript
const StorageSetupDialog = ({ open, onClose, onComplete }: Props) => {
  return (
    <Dialog open={open} onClose={onClose}>
      <DialogTitle>Storage Configuration Required</DialogTitle>
      <DialogContent>
        <Typography>
          You need to configure a default storage destination before creating jobs.
        </Typography>
        <Button 
          variant="contained" 
          onClick={() => navigate('/settings/storage')}
        >
          Set Up Storage
        </Button>
      </DialogContent>
    </Dialog>
  );
};
```

---

## 4. Regional Pricing Changes

### What Changed
- Pricing calculations now use **decimal precision** instead of doubles
- Regional multipliers are now explicitly tracked in `PricingSnapshot`
- Minimum charge enforced ($0.20 USD)

### API Response Updates

#### PricingSnapshot Changes
```typescript
interface PricingSnapshot {
  baseRatePerGb: number;           // 0.05 (base rate)
  userRegion: string;              // "EG", "US", "SA", etc.
  regionMultiplier: number;        // NEW: 0.4, 0.8, 1.0
  healthMultiplier: number;        // 0.5 - 1.5
  isCacheHit: boolean;
  totalSizeInBytes: number;        // NEW: Original file size
  calculatedSizeInGb: number;      // NEW: Rounded GB (min 0.1)
  cacheDiscountAmount?: number;    // DEPRECATED (always 0)
  finalPrice: number;              // Final price in USD
}
```

### Frontend Implementation

#### Pricing Display Component
```typescript
const PricingBreakdown = ({ snapshot }: { snapshot: PricingSnapshot }) => {
  return (
    <Card>
      <CardHeader title="Pricing Breakdown" />
      <CardContent>
        <Table>
          <TableBody>
            <TableRow>
              <TableCell>File Size</TableCell>
              <TableCell align="right">
                {formatBytes(snapshot.totalSizeInBytes)} ({snapshot.calculatedSizeInGb} GB)
              </TableCell>
            </TableRow>
            
            <TableRow>
              <TableCell>Base Rate</TableCell>
              <TableCell align="right">${snapshot.baseRatePerGb}/GB</TableCell>
            </TableRow>
            
            <TableRow>
              <TableCell>
                Region ({snapshot.userRegion})
                <Tooltip title="Regional pricing based on purchasing power parity">
                  <InfoIcon fontSize="small" />
                </Tooltip>
              </TableCell>
              <TableCell align="right">
                {snapshot.regionMultiplier === 1.0 
                  ? 'Base Price' 
                  : `${((1 - snapshot.regionMultiplier) * 100).toFixed(0)}% Off`}
              </TableCell>
            </TableRow>
            
            <TableRow>
              <TableCell>
                Torrent Health
                <Tooltip title="Based on seeders/leechers ratio">
                  <InfoIcon fontSize="small" />
                </Tooltip>
              </TableCell>
              <TableCell align="right">
                {snapshot.healthMultiplier}x
              </TableCell>
            </TableRow>
            
            <Divider />
            
            <TableRow>
              <TableCell><strong>Total Price</strong></TableCell>
              <TableCell align="right">
                <strong>${snapshot.finalPrice.toFixed(2)} USD</strong>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
        
        {snapshot.finalPrice === 0.20 && (
          <Alert severity="info" sx={{ mt: 2 }}>
            Minimum charge of $0.20 applied
          </Alert>
        )}
      </CardContent>
    </Card>
  );
};
```

#### Regional Discount Badge
```typescript
const RegionalDiscountBadge = ({ region }: { region: string }) => {
  const discounts: Record<string, number> = {
    EG: 60, // 60% off
    IN: 60,
    SA: 20,
    US: 0,
    EU: 0
  };
  
  const discount = discounts[region] || 0;
  
  if (discount === 0) return null;
  
  return (
    <Chip 
      label={`${discount}% Regional Discount`} 
      color="success" 
      size="small"
    />
  );
};
```

---

## 5. Wallet Balance Deduction with Distributed Locking

### What Changed
- **Critical Security Fix**: Race condition prevention using Redis distributed locks
- New error code: `WALLET_BUSY` when wallet is locked by another transaction
- `DeductBalanceAsync` renamed from `DetuctBalanceAync` (typo fix)

### Error Handling

#### New Error Codes
```typescript
enum WalletErrorCode {
  WALLET_BUSY = 'WALLET_BUSY',           // NEW: Wallet locked
  INSUFFICIENT_FUNDS = 'INSUFFICIENT_FUNDS',
  DEDUCTION_ERROR = 'DEDUCTION_ERROR',
  DATABASE_ERROR = 'DATABASE_ERROR'
}
```

### Frontend Implementation

#### Payment Processing with Retry
```typescript
const processPayment = async (invoiceId: number, maxRetries = 3) => {
  let attempts = 0;
  
  while (attempts < maxRetries) {
    try {
      const response = await api.post('/api/payments/process', { invoiceId });
      
      if (response.success) {
        showNotification('success', 'Payment processed successfully!');
        return response;
      }
    } catch (error) {
      if (error.code === 'WALLET_BUSY') {
        attempts++;
        
        if (attempts < maxRetries) {
          // Wait with exponential backoff
          const delay = Math.min(1000 * Math.pow(2, attempts), 5000);
          showNotification('info', 'Wallet is busy, retrying...');
          await sleep(delay);
          continue;
        } else {
          showNotification('error', 'Wallet is currently processing another transaction. Please try again.');
        }
      } else if (error.code === 'INSUFFICIENT_FUNDS') {
        showDepositDialog();
      } else {
        showNotification('error', error.message);
      }
      
      throw error;
    }
  }
};

// Helper
const sleep = (ms: number) => new Promise(resolve => setTimeout(resolve, ms));
```

#### Wallet Balance Display with Loading State
```typescript
const WalletBalance = () => {
  const [balance, setBalance] = useState<number | null>(null);
  const [isRefreshing, setIsRefreshing] = useState(false);
  const [isLocked, setIsLocked] = useState(false);
  
  const fetchBalance = async () => {
    setIsRefreshing(true);
    try {
      const response = await api.get('/api/wallet/balance');
      setBalance(response.data.balance);
      setIsLocked(false);
    } catch (error) {
      if (error.code === 'WALLET_BUSY') {
        setIsLocked(true);
      }
    } finally {
      setIsRefreshing(false);
    }
  };
  
  return (
    <Card>
      <CardContent>
        <Typography variant="h6">Wallet Balance</Typography>
        {isLocked && (
          <Alert severity="warning" sx={{ mb: 2 }}>
            Wallet is processing a transaction
          </Alert>
        )}
        <Typography variant="h4">
          ${balance?.toFixed(2) || '0.00'}
        </Typography>
        <Button 
          onClick={fetchBalance} 
          disabled={isRefreshing}
          startIcon={<RefreshIcon />}
        >
          Refresh
        </Button>
      </CardContent>
    </Card>
  );
};
```

---

## 6. Resumable Multipart Uploads (S3/Backblaze)

### What Changed
- New **Sync** entity tracks S3 sync operations
- **S3SyncProgress** tracks individual file upload progress with multipart support
- Jobs now sync to S3/Backblaze before uploading to Google Drive

### New Entities

#### Sync Entity
```typescript
interface Sync {
  id: number;
  jobId: number;
  status: 'Pending' | 'InProgress' | 'Completed' | 'Failed' | 'Retrying';
  localFilePath?: string;
  s3KeyPrefix?: string;
  totalBytes: number;
  bytesSynced: number;
  filesTotal: number;
  filesSynced: number;
  errorMessage?: string;
  startedAt?: string;
  completedAt?: string;
  retryCount: number;
  nextRetryAt?: string;
  fileProgress: S3SyncProgress[];
}
```

#### S3SyncProgress Entity
```typescript
interface S3SyncProgress {
  id: number;
  jobId: number;
  syncId: number;
  localFilePath: string;
  s3Key: string;
  uploadId?: string;          // S3 multipart upload ID
  partSize: number;           // 10MB
  totalParts: number;
  partsCompleted: number;
  bytesUploaded: number;
  totalBytes: number;
  partETags: string;          // JSON array of {PartNumber, ETag}
  status: 'NotStarted' | 'InProgress' | 'Completed' | 'Failed';
  lastPartNumber?: number;
  startedAt?: string;
  completedAt?: string;
}
```

### Frontend Implementation

#### Sync Progress Component
```typescript
const SyncProgressCard = ({ sync }: { sync: Sync }) => {
  const overallProgress = sync.totalBytes > 0 
    ? (sync.bytesSynced / sync.totalBytes) * 100 
    : 0;
  
  return (
    <Card>
      <CardHeader title="Storage Sync Progress" />
      <CardContent>
        <Box sx={{ mb: 2 }}>
          <Typography variant="body2" color="text.secondary">
            Syncing to Backblaze B2
          </Typography>
          <LinearProgress 
            variant="determinate" 
            value={overallProgress} 
            sx={{ mt: 1 }}
          />
          <Typography variant="caption" color="text.secondary">
            {sync.filesSynced} / {sync.filesTotal} files • {formatBytes(sync.bytesSynced)} / {formatBytes(sync.totalBytes)}
          </Typography>
        </Box>
        
        {/* File-level progress */}
        <Accordion>
          <AccordionSummary expandIcon={<ExpandMoreIcon />}>
            <Typography>File Details ({sync.fileProgress.length})</Typography>
          </AccordionSummary>
          <AccordionDetails>
            <List>
              {sync.fileProgress.map((file) => (
                <FileUploadProgress key={file.id} file={file} />
              ))}
            </List>
          </AccordionDetails>
        </Accordion>
        
        {sync.status === 'Failed' && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {sync.errorMessage}
          </Alert>
        )}
      </CardContent>
    </Card>
  );
};

const FileUploadProgress = ({ file }: { file: S3SyncProgress }) => {
  const progress = file.totalParts > 0 
    ? (file.partsCompleted / file.totalParts) * 100 
    : 0;
  
  return (
    <ListItem>
      <ListItemText 
        primary={file.localFilePath.split('/').pop()}
        secondary={
          <Box>
            <LinearProgress 
              variant="determinate" 
              value={progress} 
              sx={{ mb: 0.5 }}
            />
            <Typography variant="caption">
              Part {file.partsCompleted}/{file.totalParts} • {formatBytes(file.bytesUploaded)}/{formatBytes(file.totalBytes)}
            </Typography>
          </Box>
        }
      />
    </ListItem>
  );
};
```

---

## 7. Job Retry Mechanism

### What Changed
- Automatic retry with exponential backoff
- New fields: `RetryCount`, `NextRetryAt`
- Separate retry statuses: `TORRENT_DOWNLOAD_RETRY`, `UPLOAD_RETRY`, `SYNC_RETRY`

### Frontend Implementation

#### Retry Status Indicator
```typescript
const RetryIndicator = ({ job }: { job: UserJob }) => {
  if (!job.nextRetryAt) return null;
  
  const timeUntilRetry = new Date(job.nextRetryAt).getTime() - Date.now();
  
  return (
    <Alert severity="warning">
      <AlertTitle>Retrying...</AlertTitle>
      <Typography variant="body2">
        Attempt {job.retryCount + 1} scheduled in {formatDuration(timeUntilRetry)}
      </Typography>
      <LinearProgress sx={{ mt: 1 }} />
    </Alert>
  );
};
```

---

## 8. Voucher and Deposit Support

### What Changed
- Vouchers can now be applied to invoices
- Deposit tracking improved
- Admin adjustment operations now use distributed locking

### Frontend Implementation

#### Voucher Application
```typescript
const ApplyVoucherForm = ({ invoiceId }: { invoiceId: number }) => {
  const [code, setCode] = useState('');
  const [loading, setLoading] = useState(false);
  
  const applyVoucher = async () => {
    setLoading(true);
    try {
      const response = await api.post(`/api/invoices/${invoiceId}/apply-voucher`, { 
        code 
      });
      
      if (response.success) {
        showNotification('success', `Voucher applied! Discount: $${response.data.discountAmount}`);
        refreshInvoice();
      }
    } catch (error) {
      showNotification('error', error.message);
    } finally {
      setLoading(false);
    }
  };
  
  return (
    <Box sx={{ display: 'flex', gap: 1 }}>
      <TextField 
        label="Voucher Code"
        value={code}
        onChange={(e) => setCode(e.target.value.toUpperCase())}
        size="small"
      />
      <Button 
        variant="contained" 
        onClick={applyVoucher}
        disabled={!code || loading}
      >
        Apply
      </Button>
    </Box>
  );
};
```

---

## 9. Infrastructure Changes

### New Worker Services

1. **TorreClou.Worker** - Torrent downloading
2. **TorreClou.GoogleDrive.Worker** - Google Drive uploads
3. **TorreClou.Sync.Worker** - S3/Backblaze syncing

### Redis Services

- **RedisCacheService** - Caching operations
- **RedisLockService** - Distributed locking
- **RedisStreamService** - Job queue

---

## Summary of Frontend Action Items

### High Priority
1. ✅ Update job status enum with new intermediate states
2. ✅ Implement granular status display (badges, steppers)
3. ✅ Update job creation error handling (NO_STORAGE, JOB_ALREADY_EXISTS, JOB_RETRYING)
4. ✅ Add retry logic for WALLET_BUSY errors
5. ✅ Update pricing breakdown to show regional multipliers

### Medium Priority
6. ✅ Implement sync progress tracking UI
7. ✅ Add file-level multipart upload progress
8. ✅ Update job statistics dashboard
9. ✅ Add storage setup dialog/wizard

### Low Priority
10. ✅ Implement voucher application form
11. ✅ Add retry countdown indicators
12. ✅ Polish regional discount badges

---

## Testing Checklist

- [ ] Test all new job statuses display correctly
- [ ] Verify progress stepper updates in real-time
- [ ] Test wallet busy retry logic
- [ ] Verify pricing calculations match backend
- [ ] Test storage setup flow for new users
- [ ] Verify sync progress displays correctly
- [ ] Test voucher application
- [ ] Verify error handling for all new error codes

---

## API Endpoints Summary

### Modified Endpoints

| Endpoint | Changes |
|----------|---------|
| `POST /api/jobs` | Removed warning fields, added NO_STORAGE error |
| `GET /api/jobs/{id}` | Status field now enum instead of string |
| `GET /api/jobs/statistics` | New granular statistics fields |
| `POST /api/wallet/deduct` | Now uses distributed locking |
| `GET /api/pricing/calculate` | Added regional multiplier to response |

### New Endpoints (Potential)

| Endpoint | Purpose |
|----------|---------|
| `GET /api/jobs/{id}/sync` | Get sync progress details |
| `GET /api/jobs/{id}/sync/files` | Get file-level sync progress |

---

## Migration Notes

### Breaking Changes
1. Job status is now an enum, not a string
2. JobCreationResult no longer has warning fields
3. PricingSnapshot has new required fields

### Backward Compatibility
- Old job statuses (`PROCESSING`) are automatically mapped to new statuses
- Price calculations are backward compatible (same final prices)

---

**Document Version**: 1.0  
**Last Updated**: 2025-12-21  
**Backend Branch**: `rclone` / `copilot/sub-pr-2`
