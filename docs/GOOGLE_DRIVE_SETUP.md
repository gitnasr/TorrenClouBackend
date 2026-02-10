# Google Drive Setup Guide

This guide walks you through setting up Google Drive OAuth credentials for TorreClou. You'll need to create your own Google Cloud project to use Google Drive as a storage provider.

## Overview

TorreClou uses Google Drive in two ways:
1. **User Authentication** - Google OAuth for signing in
2. **Cloud Storage** - Upload downloaded torrents to user's Google Drive

Both require OAuth 2.0 credentials from Google Cloud Console.

## Prerequisites

- A Google account
- 5-10 minutes of setup time

## Step-by-Step Setup

### Step 1: Create a Google Cloud Project

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Click **Select a project** → **New Project**
3. Enter project details:
   - **Project name**: `TorreClou` (or your preferred name)
   - **Organization**: Leave as default (or select your org)
4. Click **Create**
5. Wait for the project to be created (~10 seconds)
6. Select your new project from the project dropdown

### Step 2: Enable Required APIs

1. In the left sidebar, click **APIs & Services** → **Library**
2. Search for and enable these APIs:
   - **Google Drive API**
     - Click on **Google Drive API**
     - Click **Enable**
   - **Google+ API** (for user profile)
     - Search for "Google+ API"
     - Click **Enable**

### Step 3: Configure OAuth Consent Screen

1. Go to **APIs & Services** → **OAuth consent screen**
2. Select **External** (unless you have a Google Workspace account)
3. Click **Create**
4. Fill in the **OAuth consent screen** form:

   **App information:**
   - **App name**: `TorreClou` (or your app name)
   - **User support email**: Your email address
   - **App logo**: (Optional) Upload a logo

   **App domain:**
   - **Application home page**: `http://localhost:5000` (for local development)
   - **Application privacy policy link**: (Optional)
   - **Application terms of service link**: (Optional)

   **Authorized domains:**
   - Add your production domain if deploying (e.g., `yourdomain.com`)
   - For local development, leave empty

   **Developer contact information:**
   - **Email addresses**: Your email address

5. Click **Save and Continue**

6. **Scopes** screen:
   - Click **Add or Remove Scopes**
   - Select these scopes:
     - `openid`
     - `email`
     - `profile`
     - `https://www.googleapis.com/auth/drive.file`
   - Click **Update**
   - Click **Save and Continue**

7. **Test users** (for External apps):
   - Click **Add Users**
   - Add email addresses of people who can test the app
   - Click **Save and Continue**

8. **Summary**:
   - Review your settings
   - Click **Back to Dashboard**

### Step 4: Create OAuth 2.0 Credentials

1. Go to **APIs & Services** → **Credentials**
2. Click **Create Credentials** → **OAuth client ID**
3. Configure the OAuth client:

   **Application type:** Web application

   **Name:** `TorreClou Web Client`

   **Authorized JavaScript origins:**
   - `http://localhost:3000` (your frontend URL)
   - `http://localhost:5000` (your API URL)
   - Add production URLs if deploying

   **Authorized redirect URIs:**
   - `http://localhost:5000/api/auth/google/callback` (for user login)
   - `http://localhost:5000/api/storage/gdrive/callback` (for Google Drive access)
   - Add production callback URLs if deploying

4. Click **Create**
5. **Important:** Copy the credentials shown:
   - **Client ID** - Looks like `123456789-abcdefg.apps.googleusercontent.com`
   - **Client Secret** - Looks like `GOCSPX-xxxxxxxxxxxxx`

⚠️ **Save these credentials securely!** You'll need them in the next step.

### Step 5: Configure TorreClou

1. Open your `.env` file in the TorreClou project root
2. Add your Google OAuth credentials:

```env
# Google OAuth (for user login)
GOOGLE_CLIENT_ID=123456789-abcdefg.apps.googleusercontent.com

# Google Drive OAuth (for cloud storage uploads)
GOOGLE_DRIVE_CLIENT_ID=123456789-abcdefg.apps.googleusercontent.com
GOOGLE_DRIVE_CLIENT_SECRET=GOCSPX-xxxxxxxxxxxxx
GOOGLE_DRIVE_REDIRECT_URI=http://localhost:5000/api/storage/gdrive/callback
FRONTEND_URL=http://localhost:3000
```

**Note:** You can use the **same Client ID and Secret** for both user authentication and Google Drive access.

3. Save the file

### Step 6: Test the Setup

1. Start TorreClou:
   ```bash
   docker-compose up -d
   ```

2. Open your browser and navigate to `http://localhost:3000`

3. Try to sign in with Google:
   - Click **Sign in with Google**
   - You should be redirected to Google's consent screen
   - Grant the requested permissions
   - You should be redirected back to the app

4. Connect Google Drive:
   - Go to **Storage Settings**
   - Click **Connect Google Drive**
   - Grant Google Drive permissions
   - You should see "Google Drive Connected"

If any step fails, check the troubleshooting section below.

## Production Deployment

When deploying TorreClou to production:

### 1. Update OAuth Consent Screen

Go back to **OAuth consent screen** and update:
- **Application home page**: `https://yourdomain.com`
- **Authorized domains**: Add your production domain

### 2. Add Production Redirect URIs

Go to **Credentials** → Your OAuth client → Edit:
- Add **Authorized JavaScript origins**:
  - `https://yourdomain.com`
  - `https://api.yourdomain.com`
- Add **Authorized redirect URIs**:
  - `https://api.yourdomain.com/api/auth/google/callback`
  - `https://api.yourdomain.com/api/storage/gdrive/callback`

### 3. Update Environment Variables

Update your production `.env`:
```env
GOOGLE_DRIVE_REDIRECT_URI=https://api.yourdomain.com/api/storage/gdrive/callback
FRONTEND_URL=https://yourdomain.com
```

### 4. Publish Your App (Optional)

By default, your app is in "Testing" mode and limited to 100 test users. To remove this limit:

1. Go to **OAuth consent screen**
2. Click **Publish App**
3. Submit for verification if required by Google

⚠️ **Note:** Apps requesting sensitive scopes (like Drive access) may require Google's verification, which can take several days.

## Scopes Explained

TorreClou requests these Google OAuth scopes:

| Scope | Purpose | Required |
|-------|---------|----------|
| `openid` | Verify user identity | Yes |
| `email` | Get user's email address | Yes |
| `profile` | Get user's name and picture | Yes |
| `https://www.googleapis.com/auth/drive.file` | Upload files to Google Drive | Yes (for Google Drive storage) |

**Why `drive.file` and not `drive`?**
- `drive.file` - Access only to files created by the app (secure, recommended)
- `drive` - Full access to all Drive files (not needed, avoid)

## Troubleshooting

### Error: "Access blocked: This app's request is invalid"

**Cause:** Redirect URI mismatch

**Solution:**
1. Check that the redirect URI in your `.env` matches exactly what's in Google Cloud Console
2. URIs are case-sensitive and must include protocol (`http://` or `https://`)
3. No trailing slashes

### Error: "This app hasn't been verified"

**Cause:** Using an external OAuth app in testing mode

**Solution:**
- Click **Advanced** → **Go to TorreClou (unsafe)**
- This is normal for apps in testing mode
- For production, submit for verification

### Error: "redirect_uri_mismatch"

**Cause:** The redirect URI doesn't match what's registered

**Solution:**
1. Check the error message for the actual redirect URI being used
2. Add that exact URI to **Authorized redirect URIs** in Google Cloud Console
3. Wait 5 minutes for changes to propagate

### Error: "invalid_client"

**Cause:** Client ID or Secret is incorrect

**Solution:**
1. Verify your `GOOGLE_DRIVE_CLIENT_ID` and `GOOGLE_DRIVE_CLIENT_SECRET` in `.env`
2. Make sure there are no extra spaces or quotes
3. Regenerate credentials in Google Cloud Console if needed

### Can't connect Google Drive after login

**Cause:** Missing Google Drive API scopes

**Solution:**
1. Verify Google Drive API is enabled in Google Cloud Console
2. Check OAuth consent screen includes `drive.file` scope
3. Try disconnecting and reconnecting Google Drive in the app

### "Error 429: Rate limit exceeded"

**Cause:** Too many requests to Google Drive API

**Solution:**
- Wait a few minutes before retrying
- Check if you're uploading too many files concurrently
- Consider requesting a quota increase in Google Cloud Console

## Security Best Practices

1. **Never commit credentials** - Keep `.env` out of version control
2. **Use different credentials** for development and production
3. **Rotate secrets** periodically
4. **Limit test users** in development
5. **Enable HTTPS** in production
6. **Monitor API usage** in Google Cloud Console

## API Quotas

Google Drive API has these default quotas:
- **Queries per day**: 1,000,000,000
- **Queries per 100 seconds per user**: 1,000
- **Queries per 100 seconds**: 10,000

These are usually sufficient. If you need more, request a quota increase in Google Cloud Console.

## Additional Resources

- [Google Cloud Console](https://console.cloud.google.com/)
- [Google Drive API Documentation](https://developers.google.com/drive/api/guides/about-sdk)
- [OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2)
- [Google API Scopes](https://developers.google.com/identity/protocols/oauth2/scopes)

## Need Help?

If you encounter issues not covered here:
1. Check the [TorreClou Issues](https://github.com/yourusername/torrenclo/issues)
2. Open a new issue with:
   - Error messages
   - Steps you've tried
   - Screenshots of Google Cloud Console settings

---

**Updated:** 2026-01-31
