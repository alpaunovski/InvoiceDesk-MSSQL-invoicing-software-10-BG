# Authentication, Licensing, and Admin Control

## Overview
This document describes the InvoiceDesk authentication stack: the WordPress backend plugin (`invoicedesk-auth`), the REST contract, session/licensing rules, and the desktop client flow (token storage, startup validation, and logout).

## Components
- WordPress plugin: exposes REST APIs under `/wp-json/invoicedesk/v1/`, maintains device sessions, and ships an admin UI (Users/Sessions) inside wp-admin.
- Database: custom table `wp_invoicedesk_sessions` plus two `user_meta` keys (`max_sessions`, `account_status`).
- Desktop client: C# WPF app using `AuthService` + `AuthWorkflow`, with tokens stored in Windows Credential Manager (DPAPI file fallback).

## Backend (WordPress Plugin)
- Table `wp_invoicedesk_sessions` columns: `id`, `user_id`, `token`, `device_name`, `ip_address`, `created_at`, `last_seen`, `is_active`, `expires_at`; indexed on `user_id` and `token`.
- User meta:
  - `max_sessions` (int, default 1)
  - `account_status` (`active`, `suspended`, or `disabled`)
- REST endpoints (POST):
  - `/login`: email/username + password → enforces `account_status` (`active`), prunes oldest session if limit hit, issues 32-byte hex token, 24h expiry.
  - `/validate`: Bearer token → checks active/not expired, updates `last_seen` and IP.
  - `/logout`: Bearer token → deactivates session.
  - `/sessions/list` (admin): lists active sessions with user email, device, IP, timestamps.
  - `/sessions/revoke` (admin): deactivates a session by id.
- Admin UI (wp-admin → InvoiceDesk menu):
  - Users: View **Authorized InvoiceDesk Users** list with **Remove User** button (removes them from InvoiceDesk and revokes active sessions without deleting their WordPress user account). Add any existing WordPress user to InvoiceDesk using the **Add WordPress User to InvoiceDesk** section.
  - Sessions: view active sessions; revoke a session.
- Security: HTTPS required; capability checks for admin routes; nonces on admin forms; all inputs sanitized and SQL uses `$wpdb->prepare`.

## Desktop Client Flow
- Configuration: `AuthApi` section in [InvoiceDesk/appsettings.json](../InvoiceDesk/appsettings.json) with `BaseUrl` (WordPress site) and `RequestTimeoutSeconds`.
- Startup path:
  1) `AuthWorkflow` checks cached token from `ITokenStore`.
  2) If present and not expired, calls `/validate`. On success → continue startup; on failure → clear token.
  3) If no valid token, shows `LoginWindow`. On successful `/login`, stores token + expiry and resumes startup.
- Token storage: primary in Windows Credential Manager; fallback DPAPI-encrypted file at `%LOCALAPPDATA%\InvoiceDesk\token.dat` if Credential Manager write fails.
- HTTP: `AuthService` attaches `Authorization: Bearer {token}` for validate/logout; login posts JSON `{ email, password, device_name }`.
- Device name: auto-populated from `Environment.MachineName` but can be edited before login; sent to the backend for admin visibility.
- Logout: `/logout` deactivates the current session; clearing local token happens on successful logout or failed validation.

## Licensing Rules
- Session limit: enforced by `max_sessions` per user; oldest active session is deleted when limit exceeded on login.
- Suspension: `account_status` != `active` causes `/login` to fail with 403.
- Expiry: tokens expire in 24h; `/validate` returns `valid:false` when expired and deactivates the session row.

## Setup Steps
1) Deploy and activate the WordPress plugin at `wordpress-plugin/invoicedesk-auth` on your WP instance.
2) Ensure HTTPS is enforced on the site.
3) Set `AuthApi:BaseUrl` in appsettings.json to your WP URL (ending with `/wp-json/invoicedesk/v1/`).
4) Verify admin capabilities: log into wp-admin → InvoiceDesk → Users/Sessions for license controls.
5) Run the desktop app; first run will prompt for email/password and cache the token.

## Troubleshooting
- Login succeeds then app exits: confirm SQL connection string works; authentication happens before DB init. Check `logs/app.log` for SQL errors.
- Validate fails immediately: token may be expired or pruned due to session limit; re-login.
- Admin session list empty: ensure HTTPS and that the calling user has `manage_options` capability; check nonce header/body on custom tooling.
- Token not persisting: Credential Manager write may fail; fallback DPAPI file is used. Check for warnings in `logs/app.log`.
