# Frontend Migration Prompt: Breaking Changes Update

## Overview
This document contains a detailed prompt for updating the frontend application to work with the backend changes that removed the payment system and simplified authentication. The backend has been migrated from a multi-user SaaS platform with payment processing to a self-hosted single-user system.

---

## Migration Summary

### 1. Authentication System Changes
- **REMOVED:** Google OAuth login (`POST /api/auth/google-login`)
- **ADDED:** Simple email/password login (`POST /api/auth/login`)
- **REMOVED:** User roles, OAuth provider fields, user balance/wallet
- **CHANGED:** JWT token no longer contains `role` claim

### 2. Payment System Removal
- **REMOVED:** All payment-related endpoints and DTOs
- **REMOVED:** Invoice system (no payment required for jobs)
- **REMOVED:** Wallet/balance system
- **REMOVED:** Deposit/payment endpoints
- **REMOVED:** Voucher/discount system
- **CHANGED:** Jobs are created directly without payment flow

### 3. Job Creation Flow Changes
- **REMOVED:** Two-step process (Quote → Pay Invoice → Create Job)
- **CHANGED:** Simplified to: Quote → Create Job (direct)
- **REMOVED:** `InvoiceId` from job creation response
- **REMOVED:** `IsRefunded` and `CanRefund` from job DTOs

### 4. Quote Endpoint Changes
- **REMOVED:** Pricing information from quote response
- **REMOVED:** Invoice-related fields
- **CHANGED:** Quote now returns only torrent metadata and selected files

---

## Detailed API Changes

### 1. Authentication Endpoint

#### ❌ OLD: Google OAuth Login
```http
POST /api/auth/google-login
Content-Type: application/json

{
  "idToken": "eyJhbGciOiJSUzI1NiIs...",
  "provider": "Google"
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "email": "user@example.com",
  "fullName": "John Doe",
  "currentBalance": 100.50,
  "role": "User"
}
```

#### ✅ NEW: Email/Password Login
```http
POST /api/auth/login
Content-Type: application/json

{
  "email": "admin@localhost.com",
  "password": "your-secure-password"
}
```

**Response:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIs...",
  "email": "admin@localhost.com",
  "fullName": "Admin User"
}
```

**Breaking Changes:**
- Endpoint changed from `/api/auth/google-login` to `/api/auth/login`
- Request body changed from `GoogleLoginDto` to `LoginRequestDto`
- Response no longer includes `currentBalance` and `role` fields
- Authentication credentials are now configured via environment variables (not OAuth)

**Error Responses:**
```json
// 401 Unauthorized
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Unauthorized",
  "status": 401,
  "detail": "Invalid email or password"
}
```

---

### 2. Quote Endpoint Changes

#### ❌ OLD: Quote Request
```http
POST /api/torrents/quote
Authorization: Bearer {token}
Content-Type: multipart/form-data

{
  "torrentFile": File,
  "selectedFilePaths": ["path/to/file1", "path/to/file2"],
  "voucherCode": "DISCOUNT10",  // ❌ REMOVED
  "storageProfileId": 1
}
```

#### ✅ NEW: Quote Request
```http
POST /api/torrents/quote
Authorization: Bearer {token}
Content-Type: multipart/form-data

{
  "torrentFile": File,
  "selectedFilePaths": ["path/to/file1", "path/to/file2"],  // Now nullable
  "storageProfileId": 1
}
```

**Breaking Changes:**
- `voucherCode` field removed (voucher system eliminated)
- `selectedFilePaths` is now nullable (can be `null` to select all files)

#### ❌ OLD: Quote Response
```json
{
  "isReadyToDownload": true,
  "originalAmountInUSD": 5.50,
  "finalAmountInUSD": 4.95,
  "finalAmountInNCurrency": 4.95,
  "torrentHealth": {
    "seeders": 150,
    "leechers": 20,
    "healthMultiplier": 0.9
  },
  "fileName": "example.torrent",
  "sizeInBytes": 1073741824,
  "isCached": false,
  "infoHash": "abc123...",
  "message": "Ready to download",
  "pricingDetails": {
    "basePrice": 5.00,
    "healthMultiplier": 0.9,
    "voucherDiscount": 0.55,
    "minimumChargeApplied": false
  },
  "invoiceId": 123  // ❌ REMOVED
}
```

#### ✅ NEW: Quote Response
```json
{
  "fileName": "example.torrent",
  "sizeInBytes": 1073741824,
  "infoHash": "abc123...",
  "torrentHealth": {
    "seeders": 150,
    "leechers": 20,
    "healthMultiplier": 0.9
  },
  "torrentFileId": 456,  // ✅ NEW: Use this for job creation
  "selectedFiles": ["path/to/file1", "path/to/file2"],
  "message": "Torrent analyzed successfully"
}
```

**Breaking Changes:**
- All pricing-related fields removed (`originalAmountInUSD`, `finalAmountInUSD`, `finalAmountInNCurrency`, `pricingDetails`)
- `isReadyToDownload` removed (jobs are always ready)
- `isCached` removed
- `invoiceId` removed (no invoices)
- `torrentFileId` added (required for job creation)

---

### 3. Job Creation Flow

#### ❌ OLD: Two-Step Process
```javascript
// Step 1: Get Quote (creates invoice)
const quoteResponse = await fetch('/api/torrents/quote', {
  method: 'POST',
  headers: { 'Authorization': `Bearer ${token}` },
  body: formData
});
const quote = await quoteResponse.json();
// quote.invoiceId = 123

// Step 2: Pay Invoice
const paymentResponse = await fetch('/api/invoices/pay?invoiceId=123', {
  method: 'POST',
  headers: { 'Authorization': `Bearer ${token}` }
});
const payment = await paymentResponse.json();
// payment.jobId = 456
```

#### ✅ NEW: Direct Job Creation
```javascript
// Step 1: Get Quote (analyzes torrent only)
const quoteResponse = await fetch('/api/torrents/quote', {
  method: 'POST',
  headers: { 'Authorization': `Bearer ${token}` },
  body: formData
});
const quote = await quoteResponse.json();
// quote.torrentFileId = 456

// Step 2: Create Job Directly (no payment)
const jobResponse = await fetch('/api/torrents/create-job', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${token}`,
    'Content-Type': 'application/json'
  },
  body: JSON.stringify({
    torrentFileId: quote.torrentFileId,
    selectedFilePaths: quote.selectedFiles,  // Can be null for all files
    storageProfileId: 1  // Optional
  })
});
const job = await jobResponse.json();
// job.jobId = 789
```

#### ❌ OLD: Create Job Request
```http
POST /api/jobs/create
Authorization: Bearer {token}
Content-Type: application/json

{
  "invoiceId": 123  // ❌ REMOVED
}
```

#### ✅ NEW: Create Job Request
```http
POST /api/torrents/create-job
Authorization: Bearer {token}
Content-Type: application/json

{
  "torrentFileId": 456,
  "selectedFilePaths": ["path/to/file1", "path/to/file2"],  // Optional, null = all files
  "storageProfileId": 1  // Optional
}
```

**Response:**
```json
{
  "jobId": 789,
  "storageProfileId": 1,
  "hasStorageProfileWarning": false,
  "storageProfileWarningMessage": null
}
```

**Breaking Changes:**
- Endpoint changed from `/api/jobs/create` to `/api/torrents/create-job`
- Request now requires `torrentFileId` (from quote response) instead of `invoiceId`
- `selectedFilePaths` can be `null` to select all files
- `storageProfileId` is optional
- Response no longer includes `invoiceId`

**Error Responses:**
```json
// 400 Bad Request - Invalid torrent file ID
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Torrent file not found"
}

// 404 Not Found - Torrent file doesn't exist
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.4",
  "title": "Not Found",
  "status": 404,
  "detail": "Torrent file with ID 456 not found"
}
```

---

### 4. Job DTO Changes

#### ❌ OLD: Job DTO
```json
{
  "id": 789,
  "status": "COMPLETED",
  "selectedFilePaths": ["path/to/file1"],
  "isRefunded": false,  // ❌ REMOVED
  "canRetry": true,
  "canCancel": false,
  "canRefund": false,  // ❌ REMOVED
  // ... other fields
}
```

#### ✅ NEW: Job DTO
```json
{
  "id": 789,
  "status": "COMPLETED",
  "selectedFilePaths": ["path/to/file1"],  // Now nullable
  "canRetry": true,
  "canCancel": false,
  // ... other fields (isRefunded and canRefund removed)
}
```

**Breaking Changes:**
- `isRefunded` field removed (refund system eliminated)
- `canRefund` computed property removed
- `selectedFilePaths` is now nullable (`string[] | null`)

---

### 5. Removed Endpoints

The following endpoints have been **completely removed** and should be deleted from the frontend:

#### Payment/Invoice Endpoints
```http
POST   /api/invoices/pay                    ❌ REMOVED
GET    /api/invoices                        ❌ REMOVED
GET    /api/invoices/{id}                   ❌ REMOVED
GET    /api/invoices/statistics             ❌ REMOVED
```

#### Deposit/Wallet Endpoints
```http
POST   /api/payments/deposit                ❌ REMOVED
POST   /api/payments/deposit/crypto         ❌ REMOVED
GET    /api/payments/deposits               ❌ REMOVED
GET    /api/payments/deposits/{id}          ❌ REMOVED
GET    /api/payments/wallet/balance         ❌ REMOVED
GET    /api/payments/wallet/transactions    ❌ REMOVED
POST   /api/payments/webhook/crypto         ❌ REMOVED
```

#### Admin Endpoints (if used)
```http
GET    /api/admin/deposits                  ❌ REMOVED
GET    /api/admin/dashboard                 ❌ REMOVED
POST   /api/admin/jobs/{id}/retry          ❌ REMOVED (use user endpoint)
POST   /api/admin/jobs/{id}/cancel         ❌ REMOVED (use user endpoint)
```

**Note:** Admin role-based authorization has been removed. All users have the same permissions.

---

### 6. JWT Token Changes

#### ❌ OLD: Token Claims
```json
{
  "sub": "123",
  "email": "user@example.com",
  "name": "John Doe",
  "role": "User"  // ❌ REMOVED
}
```

#### ✅ NEW: Token Claims
```json
{
  "sub": "123",  // User ID
  "email": "admin@localhost.com",
  "name": "Admin User"
  // No role claim
}
```

**Breaking Changes:**
- `role` claim removed from JWT token
- Frontend should not check for user roles
- All authenticated users have the same permissions

**Frontend Code Update:**
```typescript
// ❌ OLD: Check user role
const userRole = decodedToken.role;
if (userRole === 'Admin') {
  // Show admin features
}

// ✅ NEW: No role checking needed
// All authenticated users have the same access
```

---

### 7. User Profile/Account Changes

#### ❌ OLD: User Profile Response
```json
{
  "id": 123,
  "email": "user@example.com",
  "fullName": "John Doe",
  "currentBalance": 100.50,  // ❌ REMOVED
  "role": "User",  // ❌ REMOVED
  "region": "Global",  // ❌ REMOVED
  "phoneNumber": "+1234567890",  // ❌ REMOVED
  "isPhoneNumberVerified": false,  // ❌ REMOVED
  "oAuthProvider": "Google"  // ❌ REMOVED
}
```

#### ✅ NEW: User Profile (if endpoint exists)
```json
{
  "id": 123,
  "email": "admin@localhost.com",
  "fullName": "Admin User",
  "isGoogleDriveConnected": false  // Only for storage integration
}
```

**Breaking Changes:**
- All wallet/balance fields removed
- Role, region, phone number fields removed
- OAuth provider information removed
- Only basic user info remains

---

## Frontend Migration Checklist

### Authentication
- [ ] Replace Google OAuth login with email/password form
- [ ] Update login endpoint from `/api/auth/google-login` to `/api/auth/login`
- [ ] Remove `currentBalance` and `role` from auth response handling
- [ ] Remove role-based UI components and authorization checks
- [ ] Update JWT token decoding to remove role claim handling
- [ ] Update token storage/refresh logic if needed

### Payment System Removal
- [ ] Remove all invoice-related UI components
- [ ] Remove payment/deposit UI components
- [ ] Remove wallet/balance display components
- [ ] Remove voucher/discount code input fields
- [ ] Remove payment processing flows
- [ ] Remove invoice history/statistics pages
- [ ] Remove payment-related API service methods
- [ ] Remove payment-related state management (Redux/Zustand stores)

### Job Creation Flow
- [ ] Update quote endpoint to remove `voucherCode` parameter
- [ ] Update quote response handling to remove pricing fields
- [ ] Extract `torrentFileId` from quote response instead of `invoiceId`
- [ ] Remove invoice payment step from job creation flow
- [ ] Update job creation endpoint to `/api/torrents/create-job`
- [ ] Update job creation request to use `torrentFileId` instead of `invoiceId`
- [ ] Handle nullable `selectedFilePaths` (null = all files)
- [ ] Update job creation response handling (remove `invoiceId`)

### Job Display
- [ ] Remove `isRefunded` field from job display components
- [ ] Remove `canRefund` button/action from job UI
- [ ] Remove refund-related modals/dialogs
- [ ] Update job DTO TypeScript interfaces
- [ ] Handle nullable `selectedFilePaths` in job display

### API Service Layer
- [ ] Remove all invoice service methods
- [ ] Remove all payment/deposit service methods
- [ ] Remove all wallet service methods
- [ ] Remove all voucher service methods
- [ ] Update auth service to use new login endpoint
- [ ] Update job service to use new creation endpoint
- [ ] Update quote service to remove voucher parameter
- [ ] Update TypeScript interfaces/DTOs to match new API

### State Management
- [ ] Remove user balance/wallet from global state
- [ ] Remove invoice state from global state
- [ ] Remove payment state from global state
- [ ] Remove user role from global state
- [ ] Update user profile state structure

### UI/UX Updates
- [ ] Remove pricing display from quote results
- [ ] Remove "Pay Now" buttons and payment modals
- [ ] Simplify job creation flow (remove payment step)
- [ ] Update job creation success messages
- [ ] Remove balance/wallet display from header/navbar
- [ ] Remove admin-only UI elements (if any)
- [ ] Update error messages for removed endpoints

### Testing
- [ ] Update authentication tests
- [ ] Remove payment flow tests
- [ ] Update job creation tests
- [ ] Update quote endpoint tests
- [ ] Test nullable `selectedFilePaths` handling
- [ ] Test direct job creation without payment

---

## Example Frontend Code Updates

### 1. Authentication Service Update

```typescript
// ❌ OLD: Google OAuth Login
class AuthService {
  async loginWithGoogle(idToken: string) {
    const response = await fetch('/api/auth/google-login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ idToken, provider: 'Google' })
    });
    const data = await response.json();
    // data.currentBalance, data.role
    return data;
  }
}

// ✅ NEW: Email/Password Login
class AuthService {
  async login(email: string, password: string) {
    const response = await fetch('/api/auth/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password })
    });
    
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.detail || 'Login failed');
    }
    
    const data = await response.json();
    // Only: accessToken, email, fullName
    return data;
  }
}
```

### 2. Job Creation Service Update

```typescript
// ❌ OLD: Two-Step Job Creation
class JobService {
  async createJob(invoiceId: number) {
    const response = await fetch(`/api/invoices/pay?invoiceId=${invoiceId}`, {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${token}` }
    });
    const data = await response.json();
    return data.jobId;
  }
}

// ✅ NEW: Direct Job Creation
class JobService {
  async createJob(torrentFileId: number, selectedFilePaths?: string[] | null, storageProfileId?: number) {
    const response = await fetch('/api/torrents/create-job', {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        torrentFileId,
        selectedFilePaths: selectedFilePaths || null,  // null = all files
        storageProfileId
      })
    });
    
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.detail || 'Job creation failed');
    }
    
    const data = await response.json();
    return data.jobId;
  }
}
```

### 3. Quote Service Update

```typescript
// ❌ OLD: Quote with Voucher
class QuoteService {
  async getQuote(torrentFile: File, selectedFiles: string[], voucherCode?: string, storageProfileId: number) {
    const formData = new FormData();
    formData.append('torrentFile', torrentFile);
    formData.append('selectedFilePaths', JSON.stringify(selectedFiles));
    if (voucherCode) formData.append('voucherCode', voucherCode);  // ❌ REMOVED
    formData.append('storageProfileId', storageProfileId.toString());
    
    const response = await fetch('/api/torrents/quote', {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${token}` },
      body: formData
    });
    
    const quote = await response.json();
    // quote.invoiceId, quote.finalAmountInUSD, quote.pricingDetails  // ❌ REMOVED
    return quote;
  }
}

// ✅ NEW: Quote without Voucher
class QuoteService {
  async getQuote(torrentFile: File, selectedFiles: string[] | null, storageProfileId: number) {
    const formData = new FormData();
    formData.append('torrentFile', torrentFile);
    if (selectedFiles) {
      formData.append('selectedFilePaths', JSON.stringify(selectedFiles));
    }
    formData.append('storageProfileId', storageProfileId.toString());
    
    const response = await fetch('/api/torrents/quote', {
      method: 'POST',
      headers: { 'Authorization': `Bearer ${token}` },
      body: formData
    });
    
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.detail || 'Quote generation failed');
    }
    
    const quote = await response.json();
    // Only: fileName, sizeInBytes, infoHash, torrentHealth, torrentFileId, selectedFiles, message
    return quote;
  }
}
```

### 4. Job Component Update

```typescript
// ❌ OLD: Job Component with Refund
interface Job {
  id: number;
  status: string;
  isRefunded: boolean;  // ❌ REMOVED
  canRefund: boolean;  // ❌ REMOVED
  selectedFilePaths: string[];  // ❌ Not nullable
}

function JobCard({ job }: { job: Job }) {
  return (
    <div>
      <h3>Job #{job.id}</h3>
      <p>Status: {job.status}</p>
      {job.canRefund && !job.isRefunded && (
        <button onClick={() => handleRefund(job.id)}>Refund</button>  // ❌ REMOVED
      )}
    </div>
  );
}

// ✅ NEW: Job Component without Refund
interface Job {
  id: number;
  status: string;
  selectedFilePaths: string[] | null;  // ✅ Nullable
}

function JobCard({ job }: { job: Job }) {
  return (
    <div>
      <h3>Job #{job.id}</h3>
      <p>Status: {job.status}</p>
      {job.selectedFilePaths && (
        <p>Selected Files: {job.selectedFilePaths.length}</p>
      )}
      {!job.selectedFilePaths && (
        <p>All files selected</p>
      )}
    </div>
  );
}
```

### 5. Job Creation Flow Update

```typescript
// ❌ OLD: Job Creation with Payment
async function handleCreateJob(torrentFile: File) {
  // Step 1: Get quote (creates invoice)
  const quote = await quoteService.getQuote(torrentFile, selectedFiles, voucherCode, storageProfileId);
  
  // Step 2: Show pricing and payment
  showPaymentModal({
    amount: quote.finalAmountInUSD,
    invoiceId: quote.invoiceId
  });
  
  // Step 3: Pay invoice
  const payment = await paymentService.payInvoice(quote.invoiceId);
  
  // Step 4: Job created after payment
  const jobId = payment.jobId;
  navigate(`/jobs/${jobId}`);
}

// ✅ NEW: Direct Job Creation
async function handleCreateJob(torrentFile: File) {
  try {
    // Step 1: Get quote (analyzes torrent only)
    const quote = await quoteService.getQuote(torrentFile, selectedFiles, storageProfileId);
    
    // Step 2: Create job directly (no payment)
    const job = await jobService.createJob(
      quote.torrentFileId,
      quote.selectedFiles,  // Can be null for all files
      storageProfileId
    );
    
    // Step 3: Navigate to job
    navigate(`/jobs/${job.jobId}`);
  } catch (error) {
    showError(error.message);
  }
}
```

---

## TypeScript Interface Updates

### Auth Interfaces

```typescript
// ❌ OLD
interface GoogleLoginDto {
  idToken: string;
  provider: string;
}

interface AuthResponse {
  accessToken: string;
  email: string;
  fullName: string;
  currentBalance: number;  // ❌ REMOVED
  role: string;  // ❌ REMOVED
}

// ✅ NEW
interface LoginRequest {
  email: string;
  password: string;
}

interface AuthResponse {
  accessToken: string;
  email: string;
  fullName: string;
}
```

### Quote Interfaces

```typescript
// ❌ OLD
interface QuoteRequest {
  torrentFile: File;
  selectedFilePaths: string[];
  voucherCode?: string;  // ❌ REMOVED
  storageProfileId: number;
}

interface QuoteResponse {
  isReadyToDownload: boolean;  // ❌ REMOVED
  originalAmountInUSD: number;  // ❌ REMOVED
  finalAmountInUSD: number;  // ❌ REMOVED
  finalAmountInNCurrency: number;  // ❌ REMOVED
  torrentHealth: TorrentHealth;
  fileName: string;
  sizeInBytes: number;
  isCached: boolean;  // ❌ REMOVED
  infoHash: string;
  message?: string;
  pricingDetails: PricingSnapshot;  // ❌ REMOVED
  invoiceId: number;  // ❌ REMOVED
}

// ✅ NEW
interface QuoteRequest {
  torrentFile: File;
  selectedFilePaths?: string[] | null;  // ✅ Nullable
  storageProfileId: number;
}

interface QuoteResponse {
  fileName: string;
  sizeInBytes: number;
  infoHash: string;
  torrentHealth: TorrentHealth;
  torrentFileId: number;  // ✅ NEW: Use for job creation
  selectedFiles: string[];  // ✅ NEW: Confirmed selected files
  message?: string;
}
```

### Job Interfaces

```typescript
// ❌ OLD
interface CreateJobRequest {
  invoiceId: number;  // ❌ REMOVED
}

interface JobCreationResult {
  jobId: number;
  invoiceId: number;  // ❌ REMOVED
  storageProfileId?: number;
  hasStorageProfileWarning: boolean;
  storageProfileWarningMessage?: string;
}

interface Job {
  id: number;
  status: string;
  selectedFilePaths: string[];  // ❌ Not nullable
  isRefunded: boolean;  // ❌ REMOVED
  canRetry: boolean;
  canCancel: boolean;
  canRefund: boolean;  // ❌ REMOVED
  // ... other fields
}

// ✅ NEW
interface CreateJobRequest {
  torrentFileId: number;  // ✅ NEW: From quote response
  selectedFilePaths?: string[] | null;  // ✅ Optional, null = all files
  storageProfileId?: number;  // ✅ Optional
}

interface JobCreationResult {
  jobId: number;
  storageProfileId?: number;
  hasStorageProfileWarning: boolean;
  storageProfileWarningMessage?: string;
}

interface Job {
  id: number;
  status: string;
  selectedFilePaths: string[] | null;  // ✅ Nullable
  canRetry: boolean;
  canCancel: boolean;
  // ... other fields (isRefunded and canRefund removed)
}
```

---

## Error Handling Updates

### Removed Error Types
- Invoice-related errors (invoice not found, invoice expired, etc.)
- Payment-related errors (payment failed, insufficient balance, etc.)
- Wallet-related errors (balance errors, transaction errors, etc.)
- Voucher-related errors (invalid voucher, expired voucher, etc.)

### New Error Scenarios
- Invalid email/password (401 Unauthorized)
- Torrent file not found (404 Not Found)
- Invalid torrent file ID (400 Bad Request)
- Storage profile validation errors (400 Bad Request)

---

## Migration Testing Checklist

### Authentication Testing
- [ ] Test login with valid credentials
- [ ] Test login with invalid credentials (should return 401)
- [ ] Verify JWT token is received and stored
- [ ] Verify token contains correct claims (no role)
- [ ] Test token expiration handling
- [ ] Test protected endpoint access with token

### Job Creation Testing
- [ ] Test quote generation with torrent file
- [ ] Test quote with selected files
- [ ] Test quote with null selected files (all files)
- [ ] Test job creation with valid torrentFileId
- [ ] Test job creation with invalid torrentFileId (should return 404)
- [ ] Test job creation with null selectedFilePaths
- [ ] Test job creation with specific selectedFilePaths
- [ ] Verify job is created and dispatched correctly

### Job Display Testing
- [ ] Verify job list displays correctly
- [ ] Verify job details display correctly
- [ ] Verify nullable selectedFilePaths handling
- [ ] Verify canRetry works for failed jobs
- [ ] Verify canCancel works for cancellable jobs
- [ ] Verify refund UI elements are removed

### Error Handling Testing
- [ ] Test 401 errors (unauthorized)
- [ ] Test 404 errors (not found)
- [ ] Test 400 errors (bad request)
- [ ] Test network errors
- [ ] Test timeout errors

---

## Additional Notes

1. **Environment Variables**: The backend now uses environment variables for admin credentials. Ensure the frontend doesn't hardcode any credentials.

2. **Google Drive Integration**: Google Drive OAuth is still available but only for storage integration, not for authentication. This is separate from the login system.

3. **S3 Storage**: Users can now configure their own S3-compatible storage (Backblaze B2, AWS S3, etc.) via the API. This is a new feature that may need frontend support.

4. **No User Registration**: The system no longer supports user registration. Only the admin user configured via environment variables can log in.

5. **Single User System**: This is now a self-hosted, single-user system. Multi-user features have been removed.

---

## Support

If you encounter any issues during the migration:
1. Check the backend API documentation
2. Verify all endpoints are updated correctly
3. Check browser console for API errors
4. Verify JWT token is being sent in Authorization header
5. Check network tab for request/response details

---

**Last Updated:** 2026-01-25  
**Backend Version:** Post-Payment Removal & Authentication Simplification

