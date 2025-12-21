# Frontend Migration Guide: Backend API Changes

## Overview
This document outlines all backend changes that require frontend updates. The changes include major refactoring of file selection, introduction of storage profiles, sync jobs system separation, and various API improvements.

---

## 1. File Selection System - Breaking Change

### What Changed
The file selection mechanism has been completely refactored from using **numeric indices** to using **file paths** (strings).

### Impact Areas
- Quote request submission
- Job creation and display
- File selection UI components
- Any components that display or manipulate selected files

### Required Frontend Updates

#### Quote Request API (`/api/torrents/quote`)
- **Before**: `selectedFileIndices` was an array of integers (e.g., `[0, 1, 3]`)
- **After**: `selectedFilePaths` is an array of strings (e.g., `["folder/file1.txt", "folder/file2.txt"]`)
- The `selectedFilePaths` field is now required (not optional)
- You must send the full file path as it appears in the torrent file structure
- **Important Default Behavior**: If the user wants to download all files (no specific selection), you should send an array containing ALL file paths from the torrent. An empty array `[]` should never be sent. When no files are explicitly selected by the user, automatically populate `selectedFilePaths` with all available file paths from the `TorrentInfoDto.Files` array.

#### Job DTO Response
- **Before**: Jobs returned `selectedFileIndices` as `int[]`
- **After**: Jobs return `selectedFilePaths` as `string[]`
- Update all TypeScript interfaces/types to reflect this change
- Update any UI that displays selected files to show paths instead of indices

#### Torrent File Information
- The `TorrentFileDto` still contains an `Index` field, but this is now only for reference
- Use the `Path` field from `TorrentFileDto` when building the `selectedFilePaths` array
- When a user selects files, collect the `Path` values from the selected `TorrentFileDto` objects

### Migration Steps
1. Update all TypeScript interfaces that reference `selectedFileIndices` to use `selectedFilePaths: string[]`
2. Modify file selection components to collect file paths instead of indices
3. Update quote request payload construction to use paths
4. Update job display components to show file paths instead of indices
5. Ensure file path matching logic works correctly (paths are case-sensitive and must match exactly)
6. **Implement default "all files" behavior**: When building the quote request payload, if the user hasn't explicitly selected files (or has selected "all files"), automatically populate `selectedFilePaths` with all file paths from the torrent. Never send an empty array - always include all file paths when no specific selection is made.

---

## 2. Storage Profiles System - New Feature

### What Changed
A new storage profiles system has been introduced, allowing users to manage multiple storage accounts (Google Drive, OneDrive, etc.) and select which one to use for each job.

### New API Endpoints

#### Get User Storage Profiles
- **Endpoint**: `GET /api/storage/profiles`
- **Auth**: Required (User)
- **Response**: Array of `StorageProfileDto` objects
- **Fields**:
  - `id`: Profile ID
  - `profileName`: Display name for the profile
  - `providerType`: Storage provider (GoogleDrive, OneDrive, AwsS3, Dropbox)
  - `email`: Email associated with the storage account (nullable)
  - `isDefault`: Boolean indicating if this is the default profile
  - `isActive`: Boolean indicating if the profile is currently active
  - `createdAt`: Creation timestamp

#### Get Single Storage Profile
- **Endpoint**: `GET /api/storage/profiles/{id}`
- **Auth**: Required (User)
- **Response**: `StorageProfileDetailDto` (more detailed than list endpoint)

#### Set Default Profile
- **Endpoint**: `POST /api/storage/profiles/{id}/set-default`
- **Auth**: Required (User)
- **Response**: Success/error result

#### Disconnect Profile
- **Endpoint**: `POST /api/storage/profiles/{id}/disconnect`
- **Auth**: Required (User)
- **Response**: Success/error result

### Required Frontend Updates

#### Quote Request Changes
- **New Required Field**: `storageProfileId` (integer)
- The quote request now requires users to select a storage profile before requesting a quote
- You must fetch available storage profiles and present them in a selection UI
- If a user has a default profile, pre-select it in the quote form
- Validate that the selected profile is active before allowing quote submission

#### Job Display Updates
- Jobs now include `storageProfileId` and `storageProfileName` fields
- Update job list and detail views to display which storage profile was used
- Consider adding a filter or grouping option by storage profile

#### New UI Components Needed
1. **Storage Profiles Management Page**
   - List all user's storage profiles
   - Show profile details (name, provider, email, status)
   - Allow setting default profile
   - Allow disconnecting profiles
   - Show connection status and last sync time if available

2. **Storage Profile Selector**
   - Dropdown or selection component for choosing storage profile
   - Should be integrated into:
     - Quote request form
     - Job creation flow (if applicable)
   - Display profile name, provider type, and default indicator
   - Show active/inactive status
   - Disable inactive profiles in selection

3. **Storage Profile Connection Flow**
   - UI for connecting new storage accounts (Google Drive OAuth, etc.)
   - This may involve redirect flows for OAuth providers
   - Handle connection success/failure states

### Integration Points
- Before showing the quote request form, fetch available storage profiles
- If no active profiles exist, redirect to storage profile connection flow
- Store the selected `storageProfileId` in the quote request payload
- Display storage profile information in job details and lists

---

## 3. Sync Jobs System - New Feature

### What Changed
Sync jobs have been separated from regular torrent jobs into their own system with dedicated endpoints and status tracking.

### New Enum: SyncStatus
A new `SyncStatus` enum has been introduced with the following values:
- `SYNC_RETRY`: Job is retrying after a failure
- `SYNCING`: Job is actively syncing
- `FAILED`: Sync job has failed
- `COMPLETED`: Sync job completed successfully
- `NotStarted`: Sync job created but not started

**Note**: The `InProgress` status has been removed. Use `SYNCING` to indicate a job that is actively in progress.

**Important**: This is separate from `JobStatus` - do not confuse them.

### New API Endpoints (Admin Only)

#### Get All Sync Jobs
- **Endpoint**: `GET /api/admin/jobs/sync`
- **Auth**: Required (Admin)
- **Query Parameters**:
  - `pageNumber` (default: 1)
  - `pageSize` (default: 10)
  - `status` (optional, filter by SyncStatus)
- **Response**: Paginated result of `SyncJobDto` objects

#### Get Single Sync Job
- **Endpoint**: `GET /api/admin/jobs/sync/{id}`
- **Auth**: Required (Admin)
- **Response**: `SyncJobDto` object

#### Get Sync Job Statistics
- **Endpoint**: `GET /api/admin/jobs/sync/statistics`
- **Auth**: Required (Admin)
- **Response**: `SyncJobStatisticsDto` with counts for different statuses

### SyncJobDto Structure
- `id`: Sync job ID
- `jobId`: Related user job ID
- `userId`: User who owns the job
- `userEmail`: User's email
- `status`: SyncStatus enum value
- `localFilePath`: Path to local file being synced
- `s3KeyPrefix`: S3 key prefix for uploaded files
- `totalBytes`: Total size in bytes
- `bytesSynced`: Bytes synced so far
- `filesTotal`: Total number of files
- `filesSynced`: Number of files synced
- `errorMessage`: Error message if failed (nullable)
- `startedAt`: When sync started (nullable)
- `completedAt`: When sync completed (nullable)
- `retryCount`: Number of retry attempts
- `nextRetryAt`: When next retry will occur (nullable)
- `lastHeartbeat`: Last activity timestamp (nullable)
- `createdAt`: Creation timestamp
- `updatedAt`: Last update timestamp
- `requestFileName`: Name of the requested file (nullable)
- `requestFileId`: ID of the requested file (nullable)
- `storageProfileName`: Name of storage profile used (nullable)
- `storageProfileId`: ID of storage profile used (nullable)
- `progressPercentage`: Computed property (0-100)
- `isActive`: Computed boolean indicating if job is active (true when status is `SYNCING` or `SYNC_RETRY`)

### Required Frontend Updates

#### Admin Dashboard
- Add a new section or page for "Sync Jobs" management
- Display sync jobs in a table with filtering by status
- Show sync progress, file counts, and error messages
- Implement pagination for sync jobs list
- Add statistics dashboard showing:
  - Total sync jobs
  - Active sync jobs
  - Completed sync jobs
  - Failed sync jobs
  - Jobs by status breakdown

#### UI Components Needed
1. **Sync Jobs List View**
   - Table with columns: ID, User, Status, Progress, Files, Storage Profile, Created At, Actions
   - Status badges with appropriate colors
   - Progress bars showing sync completion
   - Filter dropdown for status
   - Pagination controls

2. **Sync Job Detail View**
   - Full details of a single sync job
   - Show all fields from SyncJobDto
   - Display error messages prominently if failed
   - Show retry information if applicable
   - Link to related user job if needed

3. **Sync Job Statistics Dashboard**
   - Cards or charts showing:
     - Total sync jobs count
     - Active sync jobs count
     - Completed sync jobs count
     - Failed sync jobs count
     - Breakdown by status (pie chart or bar chart)

### Integration Notes
- Sync jobs are admin-only features
- Regular users should not see sync job endpoints
- Sync jobs are related to user jobs but tracked separately
- Use the `SyncStatus` enum, not `JobStatus`, for sync job status
- **Important**: Do not use `InProgress` status - it has been removed. Use `SYNCING` to indicate jobs that are actively in progress

---

## 4. Job Status Enum Changes

### What Changed
The `JobStatus` enum has been cleaned up. Some statuses related to syncing have been removed because sync jobs now use their own `SyncStatus` enum.

### Removed Statuses
- `SYNCING` - No longer exists in JobStatus
- `SYNC_RETRY` - No longer exists in JobStatus

### Remaining JobStatus Values
- `QUEUED`
- `DOWNLOADING`
- `PENDING_UPLOAD`
- `UPLOADING`
- `TORRENT_DOWNLOAD_RETRY`
- `UPLOAD_RETRY`
- `COMPLETED`
- `FAILED`
- `CANCELLED`
- `TORRENT_FAILED`
- `UPLOAD_FAILED`
- `GOOGLE_DRIVE_FAILED`

### Required Frontend Updates
- Remove any UI logic that checks for `SYNCING` or `SYNC_RETRY` in JobStatus
- Update status filters to remove sync-related statuses
- Update status badges/indicators to remove sync statuses
- If you need to track sync status, use the SyncStatus enum from sync jobs endpoints instead
- Update the `IsActive` computed property logic if you were using it (it no longer includes sync statuses)

---

## 5. Jobs API Changes

### Get User Jobs Endpoint
- **Endpoint**: `GET /api/jobs`
- **Change**: The `userRole` query parameter has been removed
- **Current Parameters**:
  - `pageNumber` (default: 1)
  - `pageSize` (default: 10)
  - `status` (optional, filter by JobStatus)

### Required Frontend Updates
- Remove any code that passes `userRole` as a query parameter to the jobs endpoint
- The endpoint now automatically determines user permissions based on authentication
- Update API client functions to remove the userRole parameter

---

## 6. User Role Enum Changes

### What Changed
New user roles have been added to the `UserRole` enum.

### New Roles
- `Suspended`: User account is suspended
- `Banned`: User account is banned

### Updated Enum Values
- `User`
- `Admin`
- `Support`
- `Suspended` (new)
- `Banned` (new)

### Required Frontend Updates
- Update TypeScript enum definitions to include new roles
- Add UI handling for suspended/banned users:
  - Show appropriate messages when users with these roles try to access features
  - Display account status indicators
  - Handle role-based access control for these new roles
  - Update any role checking logic to account for suspended/banned states

---

## 7. Torrent Info DTO Changes

### What Changed
The `TorrentInfoDto` now includes health information directly in the response.

### New Fields
- `healthScore`: Numeric health score for the torrent
- `healthMultiplier`: Multiplier applied based on health
- `Health`: Object of type `TorrentHealthMeasurements` containing detailed health metrics

### Required Frontend Updates
- Update TypeScript interfaces to include new health fields
- Display health information in torrent details UI:
  - Show health score
  - Display health multiplier impact on pricing
  - Show detailed health measurements if available
- Consider adding health indicators (badges, colors) to torrent listings

---

## 8. Quote Response Changes

### What Changed
The `QuoteResponseDto` structure has been updated.

### Field Changes
- `TorrentHealth` field is now required (not nullable) - ensure your code handles this
- All other fields remain the same

### Required Frontend Updates
- Ensure TypeScript interfaces mark `TorrentHealth` as required (not optional)
- Update any null checks for `TorrentHealth` - it will always be present
- Display health information in quote response UI

---

## 9. Google Drive Authentication Changes

### What Changed
Google Drive authentication has been refactored with new DTOs and improved structure.

### New DTOs
- `OAuthState`: Contains user ID, nonce, profile name, and expiration
- `TokenResponse`: Contains access token, refresh token, expires in, and token type
- `UserInfoResponse`: Contains email, verified email status, and name

### Required Frontend Updates
- If you have Google Drive OAuth flow, update it to work with new response structures
- Handle the new token response format
- Display user info from the new UserInfoResponse structure
- Ensure OAuth state management works with the new OAuthState structure

---

## 10. General API Improvements

### Namespace and Import Cleanup
- Various unused imports have been removed
- Namespace corrections have been made
- These changes should not affect frontend, but ensure your API client is up to date

### Error Handling
- Ensure your error handling works with the updated response structures
- Check that all error responses are properly typed and handled

---

## Migration Checklist

### Critical (Breaking Changes)
- [ ] Update file selection from indices to paths in quote requests
- [ ] Update JobDto interface to use `selectedFilePaths` instead of `selectedFileIndices`
- [ ] Add `storageProfileId` to quote request payload
- [ ] Remove `userRole` parameter from jobs API calls
- [ ] Update JobStatus enum handling (remove SYNCING, SYNC_RETRY)

### New Features
- [ ] Implement storage profiles management UI
- [ ] Add storage profile selection to quote request form
- [ ] Implement admin sync jobs management UI
- [ ] Add sync job statistics dashboard (admin only)

### Data Model Updates
- [ ] Update TypeScript interfaces for all changed DTOs
- [ ] Update UserRole enum to include Suspended and Banned
- [ ] Add SyncStatus enum definition
- [ ] Update TorrentInfoDto to include health fields
- [ ] Mark TorrentHealth as required in QuoteResponseDto

### UI/UX Updates
- [ ] Update file selection UI to work with paths
- [ ] Display storage profile information in jobs
- [ ] Add health indicators to torrent displays
- [ ] Handle suspended/banned user states
- [ ] Update status badges to reflect new enum values

### Testing
- [ ] Test quote request with file paths
- [ ] Test storage profile selection and management
- [ ] Test admin sync jobs features
- [ ] Test job status filtering and display
- [ ] Test user role handling for new roles

---

## Additional Notes

### File Path Matching
- File paths are case-sensitive
- Paths must match exactly as they appear in the torrent file
- Ensure your file selection UI preserves the exact path format

### Storage Profile Requirements
- Users must have at least one active storage profile to create jobs
- Consider implementing a "Connect Storage" flow for new users
- Default profiles should be pre-selected but allow users to change

### Sync Jobs vs Regular Jobs
- Sync jobs are separate from regular torrent jobs
- They have their own status system (SyncStatus)
- Only admins can view sync jobs
- Regular users see their jobs through the regular jobs endpoint

### Backward Compatibility
- These changes are **not backward compatible**
- You must update your frontend to match the new API structure
- Consider versioning your API calls if you need to support multiple versions

---

## Support and Questions
If you encounter issues during migration or need clarification on any changes, refer to the API documentation or contact the backend team.

