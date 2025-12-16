# User Jobs Frontend Implementation Guide

This document provides comprehensive instructions for implementing the User Jobs feature using **Zod**, **Zustand**, and **React Query** with pagination support.

## Table of Contents

1. [Overview](#overview)
2. [Tech Stack](#tech-stack)
3. [API Endpoints](#api-endpoints)
4. [TypeScript Types & Zod Schemas](#typescript-types--zod-schemas)
5. [Zustand Store Setup](#zustand-store-setup)
6. [React Query Hooks](#react-query-hooks)
7. [UI Components](#ui-components)
8. [Complete Implementation](#complete-implementation)

---

## Overview

The User Jobs feature allows users to:
- View all their background jobs (torrent downloads/uploads)
- Filter jobs by status (QUEUED, PROCESSING, UPLOADING, COMPLETED, FAILED, CANCELLED)
- View job details including progress, storage profile, and file information
- Monitor active jobs with real-time progress updates
- Paginate through job history

**Key Features:**
- Paginated job list with standard response format
- Status filtering
- Real-time progress tracking for active jobs
- Job detail view
- Automatic polling for active jobs
- Optimistic updates with React Query
- Centralized state with Zustand

---

## Tech Stack

- **React Query (TanStack Query)**: Data fetching, caching, pagination, and synchronization
- **Zustand**: Global state management for UI state (filters, selected job)
- **Zod**: Schema validation for API responses and filters
- **NextAuth**: Authentication
- **TypeScript**: Type safety

### Installation

```bash
npm install @tanstack/react-query @tanstack/react-query-devtools zustand zod
# or
yarn add @tanstack/react-query @tanstack/react-query-devtools zustand zod
# or
pnpm add @tanstack/react-query @tanstack/react-query-devtools zustand zod
```

---

## API Endpoints

### Base URL
```
Development: http://localhost:7185
Production: https://your-api-domain.com
```

### 1. Get User Jobs (Paginated)

**Endpoint:** `GET /api/jobs?pageNumber={page}&pageSize={size}&status={status}`

**Authentication:** Required (JWT Bearer token)

**Query Parameters:**
- `pageNumber` (optional, default: 1): Page number (1-indexed)
- `pageSize` (optional, default: 10): Number of items per page (max: 50)
- `status` (optional): Filter by job status (`QUEUED`, `PROCESSING`, `UPLOADING`, `COMPLETED`, `FAILED`, `CANCELLED`)

**Success Response (200 OK):**
```json
{
  "items": [
    {
      "id": 1,
      "storageProfileId": 5,
      "storageProfileName": "My Google Drive",
      "status": "PROCESSING",
      "type": "Torrent",
      "requestFileId": 10,
      "requestFileName": "Ubuntu-22.04.iso.torrent",
      "errorMessage": null,
      "currentState": "Downloading files...",
      "startedAt": "2024-01-15T10:30:00Z",
      "completedAt": null,
      "lastHeartbeat": "2024-01-15T10:35:00Z",
      "bytesDownloaded": 52428800,
      "totalBytes": 104857600,
      "selectedFileIndices": [0, 1, 2],
      "createdAt": "2024-01-15T10:25:00Z",
      "updatedAt": "2024-01-15T10:35:00Z",
      "progressPercentage": 50.0,
      "isActive": true
    }
  ],
  "totalCount": 25,
  "pageNumber": 1,
  "pageSize": 10,
  "totalPages": 3,
  "hasPreviousPage": false,
  "hasNextPage": true
}
```

**Error Responses:**
- `401 Unauthorized` - Missing or invalid authentication token
- `400 Bad Request` - Invalid query parameters

### 2. Get Job by ID

**Endpoint:** `GET /api/jobs/{id}`

**Authentication:** Required (JWT Bearer token)

**Path Parameters:**
- `id`: Job ID

**Success Response (200 OK):**
```json
{
  "id": 1,
  "storageProfileId": 5,
  "storageProfileName": "My Google Drive",
  "status": "COMPLETED",
  "type": "Torrent",
  "requestFileId": 10,
  "requestFileName": "Ubuntu-22.04.iso.torrent",
  "errorMessage": null,
  "currentState": "Upload completed",
  "startedAt": "2024-01-15T10:30:00Z",
  "completedAt": "2024-01-15T11:00:00Z",
  "lastHeartbeat": "2024-01-15T11:00:00Z",
  "bytesDownloaded": 104857600,
  "totalBytes": 104857600,
  "selectedFileIndices": [0, 1, 2],
  "createdAt": "2024-01-15T10:25:00Z",
  "updatedAt": "2024-01-15T11:00:00Z",
  "progressPercentage": 100.0,
  "isActive": false
}
```

**Error Responses:**
- `401 Unauthorized` - Missing or invalid authentication token
- `404 Not Found` - Job not found or doesn't belong to user
  ```json
  {
    "code": "NOT_FOUND",
    "message": "Job not found."
  }
  ```

### 3. Get Job Statistics

**Endpoint:** `GET /api/jobs/statistics`

**Authentication:** Required (JWT Bearer token)

**Success Response (200 OK):**
```json
{
  "totalJobs": 7,
  "activeJobs": 1,
  "completedJobs": 3,
  "failedJobs": 3,
  "queuedJobs": 0,
  "processingJobs": 1,
  "uploadingJobs": 0,
  "cancelledJobs": 0
}
```

**Response Fields:**
- `totalJobs`: Total number of jobs for the user
- `activeJobs`: Number of active jobs (QUEUED + PROCESSING + UPLOADING)
- `completedJobs`: Number of completed jobs
- `failedJobs`: Number of failed jobs
- `queuedJobs`: Number of queued jobs
- `processingJobs`: Number of processing jobs
- `uploadingJobs`: Number of uploading jobs
- `cancelledJobs`: Number of cancelled jobs

**Error Responses:**
- `401 Unauthorized` - Missing or invalid authentication token

---

## TypeScript Types & Zod Schemas

### Types

```typescript
// types/jobs.ts

export enum JobStatus {
  QUEUED = 'QUEUED',
  PROCESSING = 'PROCESSING',
  UPLOADING = 'UPLOADING',
  COMPLETED = 'COMPLETED',
  FAILED = 'FAILED',
  CANCELLED = 'CANCELLED',
}

export enum JobType {
  Torrent = 'Torrent',
  Other = 'Other',
}

export interface Job {
  id: number;
  storageProfileId: number;
  storageProfileName: string | null;
  status: JobStatus;
  type: JobType;
  requestFileId: number;
  requestFileName: string | null;
  errorMessage: string | null;
  currentState: string | null;
  startedAt: string | null; // ISO 8601 date string
  completedAt: string | null; // ISO 8601 date string
  lastHeartbeat: string | null; // ISO 8601 date string
  bytesDownloaded: number;
  totalBytes: number;
  selectedFileIndices: number[];
  createdAt: string; // ISO 8601 date string
  updatedAt: string | null; // ISO 8601 date string
  progressPercentage: number;
  isActive: boolean;
}

export interface PaginatedJobs {
  items: Job[];
  totalCount: number;
  pageNumber: number;
  pageSize: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface JobsQueryParams {
  pageNumber?: number;
  pageSize?: number;
  status?: JobStatus | null;
}

export interface JobStatistics {
  totalJobs: number;
  activeJobs: number;
  completedJobs: number;
  failedJobs: number;
  queuedJobs: number;
  processingJobs: number;
  uploadingJobs: number;
  cancelledJobs: number;
}
```

### Zod Schemas

```typescript
// schemas/jobs.ts
import { z } from 'zod';

export const jobStatusSchema = z.enum([
  'QUEUED',
  'PROCESSING',
  'UPLOADING',
  'COMPLETED',
  'FAILED',
  'CANCELLED',
]);

export const jobTypeSchema = z.enum(['Torrent', 'Other']);

export const jobSchema = z.object({
  id: z.number().int().positive(),
  storageProfileId: z.number().int().positive(),
  storageProfileName: z.string().nullable(),
  status: jobStatusSchema,
  type: jobTypeSchema,
  requestFileId: z.number().int().positive(),
  requestFileName: z.string().nullable(),
  errorMessage: z.string().nullable(),
  currentState: z.string().nullable(),
  startedAt: z.string().datetime().nullable(),
  completedAt: z.string().datetime().nullable(),
  lastHeartbeat: z.string().datetime().nullable(),
  bytesDownloaded: z.number().int().nonnegative(),
  totalBytes: z.number().int().nonnegative(),
  selectedFileIndices: z.array(z.number().int().nonnegative()),
  createdAt: z.string().datetime(),
  updatedAt: z.string().datetime().nullable(),
  progressPercentage: z.number().min(0).max(100),
  isActive: z.boolean(),
});

export const paginatedJobsSchema = z.object({
  items: z.array(jobSchema),
  totalCount: z.number().int().nonnegative(),
  pageNumber: z.number().int().positive(),
  pageSize: z.number().int().positive(),
  totalPages: z.number().int().nonnegative(),
  hasPreviousPage: z.boolean(),
  hasNextPage: z.boolean(),
});

export const jobsQueryParamsSchema = z.object({
  pageNumber: z.number().int().positive().optional().default(1),
  pageSize: z.number().int().positive().min(1).max(50).optional().default(10),
  status: jobStatusSchema.nullable().optional(),
});

export const jobStatisticsSchema = z.object({
  totalJobs: z.number().int().nonnegative(),
  activeJobs: z.number().int().nonnegative(),
  completedJobs: z.number().int().nonnegative(),
  failedJobs: z.number().int().nonnegative(),
  queuedJobs: z.number().int().nonnegative(),
  processingJobs: z.number().int().nonnegative(),
  uploadingJobs: z.number().int().nonnegative(),
  cancelledJobs: z.number().int().nonnegative(),
});

export type Job = z.infer<typeof jobSchema>;
export type PaginatedJobs = z.infer<typeof paginatedJobsSchema>;
export type JobsQueryParams = z.infer<typeof jobsQueryParamsSchema>;
export type JobStatistics = z.infer<typeof jobStatisticsSchema>;
```

---

## Zustand Store Setup

```typescript
// stores/jobsStore.ts
import { create } from 'zustand';
import { devtools } from 'zustand/middleware';
import { JobStatus } from '@/types/jobs';

interface JobsUIState {
  // Filter state
  selectedStatus: JobStatus | null;
  setSelectedStatus: (status: JobStatus | null) => void;
  resetFilters: () => void;

  // Pagination state
  currentPage: number;
  pageSize: number;
  setCurrentPage: (page: number) => void;
  setPageSize: (size: number) => void;

  // Selected job for detail view
  selectedJobId: number | null;
  setSelectedJobId: (id: number | null) => void;
}

export const useJobsStore = create<JobsUIState>()(
  devtools(
    (set) => ({
      // Filter state
      selectedStatus: null,
      setSelectedStatus: (status) => set({ selectedStatus: status, currentPage: 1 }), // Reset to page 1 when filter changes
      resetFilters: () => set({ selectedStatus: null, currentPage: 1 }),

      // Pagination state
      currentPage: 1,
      pageSize: 10,
      setCurrentPage: (page) => set({ currentPage: page }),
      setPageSize: (size) => set({ pageSize: size, currentPage: 1 }), // Reset to page 1 when page size changes

      // Selected job
      selectedJobId: null,
      setSelectedJobId: (id) => set({ selectedJobId: id }),
    }),
    { name: 'JobsStore' }
  )
);
```

---

## React Query Hooks

### API Service

```typescript
// lib/api/jobs.ts
import { Job, PaginatedJobs, JobsQueryParams } from '@/types/jobs';

const API_BASE_URL = process.env.NEXT_PUBLIC_API_URL || 'http://localhost:7185';

export class JobsService {
  private async fetchWithAuth<T>(
    endpoint: string,
    options?: RequestInit
  ): Promise<T> {
    const session = await getSession(); // NextAuth getSession
    if (!session?.accessToken) {
      throw new Error('Not authenticated');
    }

    const response = await fetch(`${API_BASE_URL}${endpoint}`, {
      ...options,
      headers: {
        'Content-Type': 'application/json',
        Authorization: `Bearer ${session.accessToken}`,
        ...options?.headers,
      },
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ message: 'Unknown error' }));
      throw new Error(error.message || `HTTP ${response.status}`);
    }

    return response.json();
  }

  async getJobs(params: JobsQueryParams = {}): Promise<PaginatedJobs> {
    const searchParams = new URLSearchParams();
    if (params.pageNumber) searchParams.set('pageNumber', params.pageNumber.toString());
    if (params.pageSize) searchParams.set('pageSize', params.pageSize.toString());
    if (params.status) searchParams.set('status', params.status);

    return this.fetchWithAuth<PaginatedJobs>(`/api/jobs?${searchParams.toString()}`);
  }

  async getJob(jobId: number): Promise<Job> {
    return this.fetchWithAuth<Job>(`/api/jobs/${jobId}`);
  }

  async getStatistics(): Promise<JobStatistics> {
    return this.fetchWithAuth<JobStatistics>(`/api/jobs/statistics`);
  }
}

export const jobsService = new JobsService();
```

### React Query Hooks

```typescript
// hooks/useJobs.ts
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useSession } from 'next-auth/react';
import { jobsService } from '@/lib/api/jobs';
import { useJobsStore } from '@/stores/jobsStore';
import { JobsQueryParams, Job } from '@/types/jobs';
import { paginatedJobsSchema, jobSchema } from '@/schemas/jobs';

// Query keys
export const jobsKeys = {
  all: ['jobs'] as const,
  lists: () => [...jobsKeys.all, 'list'] as const,
  list: (params: JobsQueryParams) => [...jobsKeys.lists(), params] as const,
  details: () => [...jobsKeys.all, 'detail'] as const,
  detail: (id: number) => [...jobsKeys.details(), id] as const,
  statistics: () => [...jobsKeys.all, 'statistics'] as const,
};

/**
 * Hook to fetch paginated jobs list
 */
export function useJobs() {
  const { data: session, status } = useSession();
  const { currentPage, pageSize, selectedStatus } = useJobsStore();

  return useQuery({
    queryKey: jobsKeys.list({ pageNumber: currentPage, pageSize, status: selectedStatus }),
    queryFn: async () => {
      if (status !== 'authenticated' || !session?.accessToken) {
        throw new Error('Not authenticated');
      }

      const data = await jobsService.getJobs({
        pageNumber: currentPage,
        pageSize,
        status: selectedStatus,
      });

      // Validate with Zod
      return paginatedJobsSchema.parse(data);
    },
    enabled: status === 'authenticated',
    staleTime: 5 * 1000, // 5 seconds - jobs update frequently
    refetchInterval: (query) => {
      // Auto-refetch every 3 seconds if there are active jobs
      const data = query.state.data;
      const hasActiveJobs = data?.items.some((job) => job.isActive) ?? false;
      return hasActiveJobs ? 3000 : false;
    },
  });
}

/**
 * Hook to fetch a specific job by ID
 */
export function useJob(jobId: number | null) {
  const { data: session, status } = useSession();

  return useQuery({
    queryKey: jobsKeys.detail(jobId ?? 0),
    queryFn: async () => {
      if (status !== 'authenticated' || !session?.accessToken || !jobId) {
        throw new Error('Not authenticated or invalid job ID');
      }

      const data = await jobsService.getJob(jobId);

      // Validate with Zod
      return jobSchema.parse(data);
    },
    enabled: status === 'authenticated' && !!jobId,
    staleTime: 2 * 1000, // 2 seconds - job details update frequently
    refetchInterval: (query) => {
      // Auto-refetch every 2 seconds if job is active
      const data = query.state.data;
      return data?.isActive ? 2000 : false;
    },
  });
}

/**
 * Hook to fetch job statistics
 */
export function useJobStatistics() {
  const { data: session, status } = useSession();

  return useQuery({
    queryKey: jobsKeys.statistics(),
    queryFn: async () => {
      if (status !== 'authenticated' || !session?.accessToken) {
        throw new Error('Not authenticated');
      }

      const data = await jobsService.getStatistics();

      // Validate with Zod
      return jobStatisticsSchema.parse(data);
    },
    enabled: status === 'authenticated',
    staleTime: 10 * 1000, // 10 seconds - statistics don't change as frequently
    refetchInterval: 30 * 1000, // Refetch every 30 seconds
  });
}
```

---

## UI Components

### Job Statistics Component

```typescript
// components/jobs/JobStatistics.tsx
'use client';

import { useJobStatistics } from '@/hooks/useJobs';

export function JobStatistics() {
  const { data: statistics, isLoading, error } = useJobStatistics();

  if (isLoading) {
    return (
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
        {[1, 2, 3, 4].map((i) => (
          <div key={i} className="p-6 bg-gray-800 rounded-lg animate-pulse">
            <div className="h-4 bg-gray-700 rounded w-24 mb-4"></div>
            <div className="h-8 bg-gray-700 rounded w-16"></div>
          </div>
        ))}
      </div>
    );
  }

  if (error || !statistics) {
    return (
      <div className="p-4 bg-red-50 border border-red-200 rounded-lg">
        <p className="text-red-800">Error loading statistics: {error?.message || 'Unknown error'}</p>
      </div>
    );
  }

  const stats = [
    {
      label: 'Total Jobs',
      value: statistics.totalJobs,
      icon: '📊',
      color: 'bg-blue-500',
    },
    {
      label: 'Active',
      value: statistics.activeJobs,
      icon: '⏱️',
      color: 'bg-yellow-500',
    },
    {
      label: 'Completed',
      value: statistics.completedJobs,
      icon: '✅',
      color: 'bg-green-500',
    },
    {
      label: 'Failed',
      value: statistics.failedJobs,
      icon: '❌',
      color: 'bg-red-500',
    },
  ];

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
      {stats.map((stat) => (
        <div
          key={stat.label}
          className="p-6 bg-gray-800 rounded-lg border border-gray-700 hover:border-gray-600 transition-colors"
        >
          <div className="flex items-center justify-between mb-4">
            <span className="text-sm text-gray-400">{stat.label}</span>
            <span className="text-2xl">{stat.icon}</span>
          </div>
          <div className="text-3xl font-bold text-white">{stat.value}</div>
        </div>
      ))}
    </div>
  );
}
```

### Jobs List Component

```typescript
// components/jobs/JobsList.tsx
'use client';

import { useJobs } from '@/hooks/useJobs';
import { useJobsStore } from '@/stores/jobsStore';
import { JobStatus } from '@/types/jobs';
import { formatDistanceToNow } from 'date-fns';

const statusColors: Record<JobStatus, string> = {
  QUEUED: 'bg-gray-500',
  PROCESSING: 'bg-blue-500',
  UPLOADING: 'bg-purple-500',
  COMPLETED: 'bg-green-500',
  FAILED: 'bg-red-500',
  CANCELLED: 'bg-yellow-500',
};

const statusLabels: Record<JobStatus, string> = {
  QUEUED: 'Queued',
  PROCESSING: 'Processing',
  UPLOADING: 'Uploading',
  COMPLETED: 'Completed',
  FAILED: 'Failed',
  CANCELLED: 'Cancelled',
};

export function JobsList() {
  const { data, isLoading, error } = useJobs();
  const { selectedJobId, setSelectedJobId } = useJobsStore();

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-8">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4 bg-red-50 border border-red-200 rounded-lg">
        <p className="text-red-800">Error loading jobs: {error.message}</p>
      </div>
    );
  }

  if (!data || data.items.length === 0) {
    return (
      <div className="p-8 text-center text-gray-500">
        <p>No jobs found</p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {data.items.map((job) => (
        <div
          key={job.id}
          onClick={() => setSelectedJobId(job.id)}
          className={`p-4 border rounded-lg cursor-pointer transition-colors ${
            selectedJobId === job.id
              ? 'border-blue-500 bg-blue-50'
              : 'border-gray-200 hover:border-gray-300'
          }`}
        >
          <div className="flex items-start justify-between">
            <div className="flex-1">
              <div className="flex items-center gap-2 mb-2">
                <span
                  className={`px-2 py-1 text-xs font-semibold rounded ${statusColors[job.status]}`}
                >
                  {statusLabels[job.status]}
                </span>
                <span className="text-sm text-gray-500">
                  {job.requestFileName || `Job #${job.id}`}
                </span>
              </div>

              {job.currentState && (
                <p className="text-sm text-gray-600 mb-2">{job.currentState}</p>
              )}

              {job.isActive && job.totalBytes > 0 && (
                <div className="mb-2">
                  <div className="flex items-center justify-between text-xs text-gray-500 mb-1">
                    <span>{formatBytes(job.bytesDownloaded)} / {formatBytes(job.totalBytes)}</span>
                    <span>{job.progressPercentage.toFixed(1)}%</span>
                  </div>
                  <div className="w-full bg-gray-200 rounded-full h-2">
                    <div
                      className="bg-blue-600 h-2 rounded-full transition-all duration-300"
                      style={{ width: `${job.progressPercentage}%` }}
                    />
                  </div>
                </div>
              )}

              <div className="flex items-center gap-4 text-xs text-gray-500">
                <span>Storage: {job.storageProfileName || 'Unknown'}</span>
                <span>
                  {job.startedAt
                    ? `Started ${formatDistanceToNow(new Date(job.startedAt), { addSuffix: true })}`
                    : `Created ${formatDistanceToNow(new Date(job.createdAt), { addSuffix: true })}`}
                </span>
              </div>

              {job.errorMessage && (
                <div className="mt-2 p-2 bg-red-50 border border-red-200 rounded text-sm text-red-800">
                  {job.errorMessage}
                </div>
              )}
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`;
}
```

### Jobs Filters Component

```typescript
// components/jobs/JobsFilters.tsx
'use client';

import { useJobsStore } from '@/stores/jobsStore';
import { JobStatus } from '@/types/jobs';

const statusOptions: { value: JobStatus | null; label: string }[] = [
  { value: null, label: 'All Statuses' },
  { value: JobStatus.QUEUED, label: 'Queued' },
  { value: JobStatus.PROCESSING, label: 'Processing' },
  { value: JobStatus.UPLOADING, label: 'Uploading' },
  { value: JobStatus.COMPLETED, label: 'Completed' },
  { value: JobStatus.FAILED, label: 'Failed' },
  { value: JobStatus.CANCELLED, label: 'Cancelled' },
];

export function JobsFilters() {
  const { selectedStatus, setSelectedStatus, resetFilters } = useJobsStore();

  return (
    <div className="flex items-center gap-4 p-4 bg-gray-50 rounded-lg">
      <label className="text-sm font-medium text-gray-700">Filter by Status:</label>
      <select
        value={selectedStatus ?? ''}
        onChange={(e) => setSelectedStatus(e.target.value ? (e.target.value as JobStatus) : null)}
        className="px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-blue-500"
      >
        {statusOptions.map((option) => (
          <option key={option.value ?? 'all'} value={option.value ?? ''}>
            {option.label}
          </option>
        ))}
      </select>
      {selectedStatus && (
        <button
          onClick={resetFilters}
          className="px-3 py-2 text-sm text-gray-600 hover:text-gray-800 underline"
        >
          Clear Filter
        </button>
      )}
    </div>
  );
}
```

### Pagination Component

```typescript
// components/jobs/JobsPagination.tsx
'use client';

import { useJobs } from '@/hooks/useJobs';
import { useJobsStore } from '@/stores/jobsStore';

export function JobsPagination() {
  const { data } = useJobs();
  const { currentPage, pageSize, setCurrentPage, setPageSize } = useJobsStore();

  if (!data) return null;

  const { totalPages, hasPreviousPage, hasNextPage } = data;

  return (
    <div className="flex items-center justify-between p-4 border-t">
      <div className="flex items-center gap-2">
        <span className="text-sm text-gray-600">Items per page:</span>
        <select
          value={pageSize}
          onChange={(e) => setPageSize(Number(e.target.value))}
          className="px-2 py-1 border border-gray-300 rounded text-sm"
        >
          <option value={10}>10</option>
          <option value={20}>20</option>
          <option value={50}>50</option>
        </select>
      </div>

      <div className="flex items-center gap-2">
        <span className="text-sm text-gray-600">
          Page {currentPage} of {totalPages} ({data.totalCount} total)
        </span>
        <button
          onClick={() => setCurrentPage(currentPage - 1)}
          disabled={!hasPreviousPage}
          className="px-3 py-1 border rounded disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50"
        >
          Previous
        </button>
        <button
          onClick={() => setCurrentPage(currentPage + 1)}
          disabled={!hasNextPage}
          className="px-3 py-1 border rounded disabled:opacity-50 disabled:cursor-not-allowed hover:bg-gray-50"
        >
          Next
        </button>
      </div>
    </div>
  );
}
```

### Job Detail Component

```typescript
// components/jobs/JobDetail.tsx
'use client';

import { useJob } from '@/hooks/useJobs';
import { useJobsStore } from '@/stores/jobsStore';
import { formatDistanceToNow } from 'date-fns';

export function JobDetail() {
  const { selectedJobId } = useJobsStore();
  const { data: job, isLoading, error } = useJob(selectedJobId);

  if (!selectedJobId) {
    return (
      <div className="p-8 text-center text-gray-500">
        <p>Select a job to view details</p>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-8">
        <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (error || !job) {
    return (
      <div className="p-4 bg-red-50 border border-red-200 rounded-lg">
        <p className="text-red-800">Error loading job: {error?.message || 'Job not found'}</p>
      </div>
    );
  }

  return (
    <div className="p-6 bg-white border rounded-lg space-y-4">
      <div className="flex items-center justify-between">
        <h2 className="text-xl font-bold">Job #{job.id}</h2>
        <span className={`px-3 py-1 text-sm font-semibold rounded ${getStatusColor(job.status)}`}>
          {job.status}
        </span>
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div>
          <label className="text-sm font-medium text-gray-500">File Name</label>
          <p className="text-sm">{job.requestFileName || 'N/A'}</p>
        </div>
        <div>
          <label className="text-sm font-medium text-gray-500">Storage Profile</label>
          <p className="text-sm">{job.storageProfileName || 'N/A'}</p>
        </div>
        <div>
          <label className="text-sm font-medium text-gray-500">Type</label>
          <p className="text-sm">{job.type}</p>
        </div>
        <div>
          <label className="text-sm font-medium text-gray-500">Created</label>
          <p className="text-sm">
            {formatDistanceToNow(new Date(job.createdAt), { addSuffix: true })}
          </p>
        </div>
        {job.startedAt && (
          <div>
            <label className="text-sm font-medium text-gray-500">Started</label>
            <p className="text-sm">
              {formatDistanceToNow(new Date(job.startedAt), { addSuffix: true })}
            </p>
          </div>
        )}
        {job.completedAt && (
          <div>
            <label className="text-sm font-medium text-gray-500">Completed</label>
            <p className="text-sm">
              {formatDistanceToNow(new Date(job.completedAt), { addSuffix: true })}
            </p>
          </div>
        )}
      </div>

      {job.isActive && job.totalBytes > 0 && (
        <div>
          <div className="flex items-center justify-between text-sm mb-2">
            <span className="text-gray-600">Progress</span>
            <span className="text-gray-600">
              {formatBytes(job.bytesDownloaded)} / {formatBytes(job.totalBytes)} ({job.progressPercentage.toFixed(1)}%)
            </span>
          </div>
          <div className="w-full bg-gray-200 rounded-full h-3">
            <div
              className="bg-blue-600 h-3 rounded-full transition-all duration-300"
              style={{ width: `${job.progressPercentage}%` }}
            />
          </div>
        </div>
      )}

      {job.currentState && (
        <div>
          <label className="text-sm font-medium text-gray-500">Current State</label>
          <p className="text-sm">{job.currentState}</p>
        </div>
      )}

      {job.errorMessage && (
        <div className="p-4 bg-red-50 border border-red-200 rounded">
          <label className="text-sm font-medium text-red-800">Error Message</label>
          <p className="text-sm text-red-800 mt-1">{job.errorMessage}</p>
        </div>
      )}

      {job.selectedFileIndices.length > 0 && (
        <div>
          <label className="text-sm font-medium text-gray-500">Selected Files</label>
          <p className="text-sm">{job.selectedFileIndices.join(', ')}</p>
        </div>
      )}
    </div>
  );
}

function getStatusColor(status: string): string {
  const colors: Record<string, string> = {
    QUEUED: 'bg-gray-500 text-white',
    PROCESSING: 'bg-blue-500 text-white',
    UPLOADING: 'bg-purple-500 text-white',
    COMPLETED: 'bg-green-500 text-white',
    FAILED: 'bg-red-500 text-white',
    CANCELLED: 'bg-yellow-500 text-white',
  };
  return colors[status] || 'bg-gray-500 text-white';
}

function formatBytes(bytes: number): string {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB', 'TB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(2))} ${sizes[i]}`;
}
```

---

## Complete Implementation

### Main Jobs Page

```typescript
// app/jobs/page.tsx
'use client';

import { useEffect } from 'react';
import { useSession } from 'next-auth/react';
import { useRouter } from 'next/navigation';
import { JobStatistics } from '@/components/jobs/JobStatistics';
import { JobsList } from '@/components/jobs/JobsList';
import { JobsFilters } from '@/components/jobs/JobsFilters';
import { JobsPagination } from '@/components/jobs/JobsPagination';
import { JobDetail } from '@/components/jobs/JobDetail';
import { useJobsStore } from '@/stores/jobsStore';

export default function JobsPage() {
  const { data: session, status } = useSession();
  const router = useRouter();
  const { selectedJobId } = useJobsStore();

  useEffect(() => {
    if (status === 'unauthenticated') {
      router.push('/auth/signin');
    }
  }, [status, router]);

  if (status === 'loading') {
    return (
      <div className="flex items-center justify-center min-h-screen">
        <div className="animate-spin rounded-full h-12 w-12 border-b-2 border-blue-600"></div>
      </div>
    );
  }

  if (status === 'unauthenticated') {
    return null;
  }

  return (
    <div className="container mx-auto px-4 py-8">
      <div className="mb-8">
        <h1 className="text-3xl font-bold mb-2">My Jobs</h1>
        <p className="text-gray-400">Manage and track your download jobs</p>
      </div>

      <div className="mb-6">
        <JobStatistics />
      </div>

      <div className="mb-6">
        <JobsFilters />
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className={selectedJobId ? 'lg:col-span-2' : 'lg:col-span-3'}>
          <JobsList />
          <JobsPagination />
        </div>

        {selectedJobId && (
          <div className="lg:col-span-1">
            <JobDetail />
          </div>
        )}
      </div>
    </div>
  );
}
```

### Query Client Setup

```typescript
// app/providers.tsx
'use client';

import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { useState } from 'react';

export function Providers({ children }: { children: React.ReactNode }) {
  const [queryClient] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            staleTime: 60 * 1000, // 1 minute
            refetchOnWindowFocus: false,
            retry: 1,
          },
        },
      })
  );

  return (
    <QueryClientProvider client={queryClient}>
      {children}
      <ReactQueryDevtools initialIsOpen={false} />
    </QueryClientProvider>
  );
}
```

---

## Key Implementation Notes

1. **Pagination**: Uses standard `PaginatedResult<T>` response format with `pageNumber`, `pageSize`, `totalCount`, `totalPages`, `hasPreviousPage`, `hasNextPage`.

2. **Auto-refetching**: Active jobs (status: QUEUED, PROCESSING, UPLOADING) automatically refetch every 2-3 seconds to show real-time progress.

3. **Status Filtering**: Filter state is managed in Zustand and resets pagination to page 1 when changed.

4. **Zod Validation**: All API responses are validated with Zod schemas for type safety.

5. **Optimistic Updates**: React Query handles caching and automatic refetching based on query keys.

6. **Error Handling**: Proper error states and loading indicators throughout.

7. **Responsive Design**: Grid layout adapts based on whether a job is selected for detail view.

---

## Testing Checklist

- [ ] Jobs list loads with pagination
- [ ] Status filter works and resets to page 1
- [ ] Pagination controls work (Previous/Next, page size)
- [ ] Job detail view shows all information
- [ ] Active jobs auto-refetch and show progress
- [ ] Error states display correctly
- [ ] Loading states display correctly
- [ ] Empty state displays when no jobs found
- [ ] Responsive layout works on mobile/tablet/desktop

---

This implementation provides a complete, production-ready jobs management interface with pagination, filtering, real-time updates, and proper state management using Zod, Zustand, and React Query.
