# TorreClou Backend API Changes - Frontend Integration Guide

This document describes all backend API changes made during the open-source conversion.
Use this as a reference to update the frontend application.

---

## 1. REMOVED ENDPOINTS (Delete from Frontend)

### Payment & Pricing (Removed)
| Method | Endpoint | Reason |
|--------|----------|--------|
| POST | `/api/payments/*` | Payment system removed |
| GET | `/api/wallet/*` | Wallet system removed |
| POST | `/api/vouchers/*` | Voucher system removed |
| GET | `/api/pricing/*` | Pricing engine removed |
| GET | `/api/admin/analytics` | SaaS analytics removed |

### Actions Required:
- Remove all payment-related pages and components
- Remove wallet balance display
- Remove voucher input fields
- Remove pricing tier selectors
- Remove admin analytics dashboard

---

## 2. RENAMED ENDPOINTS

### Torrent Analysis (Was: Quote)

| Old | New |
|-----|-----|
| `POST /api/torrents/quote` | `POST /api/torrents/analyze` |

**Request DTO Change:**
```typescript
// OLD: QuoteRequestDto
interface QuoteRequestDto {
  torrentFile: File;
  storageProfileId: number;
  selectedFilePaths?: string[];
}

// NEW: AnalyzeTorrentRequestDto (same structure, different name)
interface AnalyzeTorrentRequestDto {
  torrentFile: File;
  storageProfileId: number;
  selectedFilePaths?: string[];
}
```

**Response DTO (Pricing fields removed):**
```typescript
interface AnalyzeResponse {
  torrentInfo: TorrentInfoDto;
  selectedSize: number;
  totalSize: number;
  // REMOVED: price, currency, discount, discountCode, finalPrice
}
```

**Action Required:**
- Rename API call from `/api/torrents/quote` to `/api/torrents/analyze`
- Remove any price/cost display from the UI
- Keep file selection and size display

---

## 3. NEW ENDPOINTS

### Google Drive Configuration (NEW - Major Change)

**Endpoint:** `POST /api/storage/gdrive/configure`

**Purpose:** Users now provide their own Google Cloud OAuth credentials instead of using environment-configured credentials.

**Request:**
```typescript
interface ConfigureGoogleDriveRequest {
  profileName: string;        // e.g., "My Google Drive"
  clientId: string;           // Google OAuth Client ID (ends with .apps.googleusercontent.com)
  clientSecret: string;       // Google OAuth Client Secret (starts with GOCSPX-)
  redirectUri: string;        // OAuth callback URL (must match Google Console)
  setAsDefault?: boolean;     // Make this the default storage
}
```

**Response:**
```typescript
interface GoogleDriveAuthResponse {
  authorizationUrl: string;   // Redirect user here to complete OAuth
}
```

**Error Response:**
```typescript
interface ErrorResponse {
  error: string;   // Error message
  code: string;    // Error code (e.g., "INVALID_CLIENT_ID")
}
```

**Flow:**
1. User fills form with their Google Cloud credentials
2. Frontend calls `POST /api/storage/gdrive/configure`
3. Backend validates credentials format and returns `authorizationUrl`
4. Frontend redirects user to `authorizationUrl`
5. User authorizes on Google
6. Google redirects to callback URL
7. Backend stores credentials, redirects to frontend success page

**Action Required:**
- Create new "Add Google Drive" form with fields:
  - Profile Name (text input)
  - Client ID (text input with validation: must contain `.apps.googleusercontent.com`)
  - Client Secret (password input with validation: should start with `GOCSPX-`)
  - Redirect URI (text input, auto-populated with `{backendUrl}/api/storage/gdrive/callback`)
  - Set as Default (checkbox)
- Add link to Google Cloud Console setup guide
- Handle redirect flow after form submission

### S3 Configuration (Existing, Reference)

**Endpoint:** `POST /api/storage/configure-s3`

**Request:**
```typescript
interface ConfigureS3Request {
  profileName: string;
  s3Endpoint: string;         // e.g., "https://s3.amazonaws.com"
  s3AccessKey: string;
  s3SecretKey: string;
  s3BucketName: string;
  s3Region: string;           // e.g., "us-east-1"
  setAsDefault?: boolean;
}
```

**Response:**
```typescript
interface StorageProfileResult {
  id: number;
  profileName: string;
  providerType: "AwsS3";
  isDefault: boolean;
}
```

---

## 4. CHANGED ENDPOINTS

### Health Check Endpoints

| Endpoint | Old Behavior | New Behavior |
|----------|--------------|--------------|
| `GET /api/health` | Checked all dependencies | Fast liveness probe (always healthy) |
| `GET /api/health/ready` | N/A (new) | Readiness probe with 10s cache |
| `GET /api/health/detailed` | N/A (new) | Full diagnostic info (for debugging) |

**Response Types:**

```typescript
// GET /api/health
interface HealthResponse {
  status: "healthy";
  timestamp: string;  // ISO 8601
}

// GET /api/health/ready
interface ReadinessResponse {
  timestamp: string;
  isHealthy: boolean;
  database: "healthy" | "unhealthy" | "unknown";
  redis: "healthy" | "unhealthy" | "unknown";
  version: string;
}

// GET /api/health/detailed
interface DetailedHealthResponse extends ReadinessResponse {
  workers: Record<string, string>;
  storage: {
    totalSpace: number;
    usedSpace: number;
    freeSpace: number;
  };
}
```

**Action Required:**
- Update health check polling to use `/api/health/ready` for status indicators
- Use `/api/health/detailed` for admin/debug views only

---

## 5. STORAGE PROFILE CHANGES

### Google Drive Flow Changed

**OLD Flow (Environment Credentials):**
1. Click "Connect Google Drive"
2. GET `/api/storage/gdrive/connect` → returns `authorizationUrl`
3. Redirect to Google
4. OAuth callback handled
5. Profile created automatically

**NEW Flow (User Credentials):**
1. User enters their Google Cloud OAuth credentials
2. POST `/api/storage/gdrive/configure` with credentials → returns `authorizationUrl`
3. Redirect to Google
4. OAuth callback handled
5. Profile created with user's credentials stored securely

**Action Required:**
- Replace simple "Connect" button with credential input form
- Add setup instructions/guide for creating Google Cloud project
- Validate credential format before submission
- The legacy `GET /api/storage/gdrive/connect` endpoint still works for backward compatibility but should be deprecated

### Storage Profile List

**Endpoint:** `GET /api/storage/profiles`

**Response:**
```typescript
interface StorageProfile {
  id: number;
  profileName: string;
  providerType: "GoogleDrive" | "AwsS3";
  email?: string;              // Google account email (for Google Drive)
  isDefault: boolean;
  isActive: boolean;
  createdAt: string;
}

type StorageProfilesResponse = StorageProfile[];
```

### Set Default Profile

**Endpoint:** `POST /api/storage/profiles/{id}/set-default`

**Response:** `StorageProfile`

### Disconnect Profile

**Endpoint:** `POST /api/storage/profiles/{id}/disconnect`

**Response:** `{ success: true }`

---

## 6. AUTHENTICATION (No Changes)

Authentication remains the same:
- `POST /api/auth/google` - Google OAuth login
- JWT tokens in `Authorization: Bearer {token}` header
- Token refresh handled automatically

---

## 7. JOB ENDPOINTS (No Changes)

Job-related endpoints remain unchanged:
- `POST /api/torrents/start-job` - Start download/upload job
- `GET /api/jobs` - List user's jobs
- `GET /api/jobs/{id}` - Get job details
- `GET /api/jobs/{id}/progress` - Real-time progress (Redis Streams)

---

## 8. REMOVED FIELDS FROM RESPONSES

### AnalyzeResponse (formerly QuoteResponse)

**Removed Fields:**
```typescript
// These fields no longer exist in response:
{
  price: number;        // REMOVED - no pricing
  currency: string;     // REMOVED - no pricing
  discount: number;     // REMOVED - no vouchers
  discountCode: string; // REMOVED - no vouchers
  finalPrice: number;   // REMOVED - no pricing
}
```

### User Profile

**Removed Fields:**
```typescript
{
  walletBalance: number;    // REMOVED - no wallet
  totalSpent: number;       // REMOVED - no payments
  activeVouchers: [];       // REMOVED - no vouchers
}
```

---

## 9. ERROR RESPONSE FORMAT

Standard error format remains:
```typescript
interface ErrorResponse {
  error: string;           // Error message
  code?: string;           // Error code (e.g., "INVALID_CLIENT_ID")
  details?: string[];      // Additional details
}
```

### New Error Codes for Google Drive Configuration:

| Code | Message | User Action |
|------|---------|-------------|
| `INVALID_CLIENT_ID` | Invalid Google Client ID format | Check Client ID ends with `.apps.googleusercontent.com` |
| `INVALID_CLIENT_SECRET` | Client Secret is required | Enter Client Secret |
| `INVALID_REDIRECT_URI` | Redirect URI is required | Enter Redirect URI |
| `DUPLICATE_EMAIL` | This Google account is already connected | Use different Google account or disconnect existing |
| `TOKEN_EXCHANGE_FAILED` | Failed to exchange code for tokens | Check credentials match Google Console |
| `INVALID_STATE` | Invalid or expired OAuth state | Restart the OAuth flow |

---

## 10. UI/UX RECOMMENDATIONS

### Storage Configuration Page

```
+-----------------------------------------------------+
| Add Storage Provider                                 |
+-----------------------------------------------------+
|                                                      |
|  [Google Drive]  [AWS S3]   <- Provider tabs        |
|                                                      |
|  +- Google Drive Setup --------------------------+  |
|  |                                               |  |
|  |  (i) You need your own Google Cloud project. |  |
|  |  [View Setup Guide]                           |  |
|  |                                               |  |
|  |  Profile Name:                                |  |
|  |  [________________________]                   |  |
|  |                                               |  |
|  |  Client ID:                                   |  |
|  |  [________________________.apps.googleuser...] |  |
|  |                                               |  |
|  |  Client Secret:                               |  |
|  |  [************************]                   |  |
|  |                                               |  |
|  |  Redirect URI:                                |  |
|  |  [http://localhost:5000/api/storage/gdrive...] |  |
|  |                                               |  |
|  |  [ ] Set as default storage                  |  |
|  |                                               |  |
|  |  [Connect Google Drive]                       |  |
|  +-----------------------------------------------+  |
|                                                      |
+-----------------------------------------------------+
```

### Remove These UI Elements:
- Wallet balance display
- "Add funds" button
- Voucher/promo code input
- Price display in torrent analysis
- Payment history page
- Subscription/plan selectors

---

## 11. ENVIRONMENT CHANGES

### Removed Environment Variables (Frontend doesn't need these):
```env
# These are no longer used by the backend:
GOOGLE_DRIVE_CLIENT_ID      # Now from user input
GOOGLE_DRIVE_CLIENT_SECRET  # Now from user input
GOOGLE_DRIVE_REDIRECT_URI   # Now from user input
```

### Required Frontend Config:
```env
NEXT_PUBLIC_API_URL=http://localhost:5000    # Backend API URL
NEXT_PUBLIC_GOOGLE_CLIENT_ID=xxx             # For user login only (unchanged)
```

---

## 12. MIGRATION CHECKLIST FOR FRONTEND

### High Priority:
- [ ] Rename `/api/torrents/quote` to `/api/torrents/analyze`
- [ ] Create Google Drive configuration form with credential inputs
- [ ] Remove all pricing/payment UI components
- [ ] Update health check polling endpoint

### Medium Priority:
- [ ] Add Google Cloud setup guide link
- [ ] Validate Google OAuth credential format in form
- [ ] Update storage profile list to show provider type
- [ ] Remove wallet balance from user profile

### Low Priority:
- [ ] Remove admin analytics pages (if any)
- [ ] Update error handling for new error codes
- [ ] Add loading states for OAuth redirect flow

---

## 13. API BASE URL

**Development:** `http://localhost:5000`
**Production:** Configure via `NEXT_PUBLIC_API_URL`

All endpoints are prefixed with `/api/`.

---

## 14. CORS

CORS is configured to allow origins specified in `ALLOWED_ORIGINS` environment variable.

For development: `http://localhost:3000`

---

## 15. EXAMPLE API CALLS

### Configure Google Drive
```typescript
const response = await fetch('/api/storage/gdrive/configure', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${token}`
  },
  body: JSON.stringify({
    profileName: 'My Google Drive',
    clientId: '123456789-abcdef.apps.googleusercontent.com',
    clientSecret: 'GOCSPX-xxxxxxxxxxxxx',
    redirectUri: 'http://localhost:5000/api/storage/gdrive/callback',
    setAsDefault: true
  })
});

const data = await response.json();
if (response.ok) {
  // Redirect user to Google OAuth
  window.location.href = data.authorizationUrl;
} else {
  // Handle error
  console.error(data.error, data.code);
}
```

### Analyze Torrent
```typescript
const formData = new FormData();
formData.append('torrentFile', file);
formData.append('storageProfileId', profileId.toString());
formData.append('selectedFilePaths', JSON.stringify(selectedPaths));

const response = await fetch('/api/torrents/analyze', {
  method: 'POST',
  headers: {
    'Authorization': `Bearer ${token}`
  },
  body: formData
});

const data = await response.json();
// data contains: torrentInfo, selectedSize, totalSize
// NO pricing fields
```

---

*Document Version: 1.0*
*Last Updated: 2026-02-01*
