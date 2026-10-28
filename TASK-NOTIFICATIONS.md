# Task push notifications

## Implemented flow

The user configured Firebase appzon-employee-app-25dab, registered Android package com.goriparthivenkat.EmployeeApp, and uploaded its FCM V1 sending credentials to EAS. Firebase private keys are not needed in this backend or the APK. The frontend includes the public google-services.json configuration and the Expo Notifications plugin.

After login/session restoration, the frontend requests notification permission, obtains an Expo push token and registers the installation using authenticated POST /api/push-tokens. The backend chooses the account from the validated JWT, never a supplied UserId. A unique token index plus a PostgreSQL upsert handles repeat registrations and account switching. Account row locking and the session version prevent an invalidated login from binding a device after logout.

Registration retries on app foreground, native token rotation, and Account → Enable / retry notifications. Registration failures do not block login. Permission denial unregisters a previously saved device token when the app can reach the server. Logout/password changes/account updates invalidate sessions and clear the corresponding account's device registrations. As with any remote operation, offline logout cannot guarantee immediate server cleanup; generic notification text avoids exposing task contents, and task access is always checked server-side.

After a task is saved, a new assignment sends a generic notification to active devices belonging to the assigned employee. Reassigning to a different employee notifies that new assignee. Ordinary title/status edits do not send duplicate assignment alerts. The task remains saved even if notifications fail; manager responses contain notificationWarning when no device is registered or sending fails.

The destination is https://exp.host/--/api/v2/push/send. Data sent includes the Expo device token, task ID, employee ID, recipient account ID, generic title/body and tasks channel. It does not include employee email/password, selfie, or private task description/title. The user explicitly approved this data flow.

Provider tickets indicate acceptance, not phone delivery. A background worker checks receipts after approximately 15 minutes and removes DeviceNotRegistered tokens using a conditional delete that cannot remove a newly rebound installation. Pending receipt tracking is bounded and in memory: restarts lose pending receipt checks. This is not a persistent outbox or guaranteed delivery/retry system. Network/provider failures are reported to the manager; employees can still view assigned tasks in the app.

Notification taps carry only structured task IDs. The app validates the signed-in recipient and employee IDs, opens the protected Tasks route, and fetches current authorized task data. Arbitrary URLs in notification data are not followed. Foreground alerts for other accounts or logged-out sessions are suppressed.

## Database

Neon already contains the five-column DevicePushTokens table created by the user: Id, UserId, PushToken, Platform and UpdatedAt, with a unique PushToken index and a UserId foreign key to Users using cascade deletion. No further runtime columns are required by this implementation.

The hand-authored idempotent migration 20261028070000_AddDevicePushTokens and model snapshot match that table. Windows Application Control blocked local EF assembly loading, so the migration was authored as source rather than scaffolded by running the blocked assembly. On an approved development host, normal EF database update can apply it safely to the existing table. Alternatively device-push-tokens-migration.sql creates missing objects and records this matching migration; it does not reset data. Do not run the old destructive account reset as a notification fix.

## Deployment

1. Deploy the updated backend. GET /api/auth/test reports apiVersion task-push-v1.
2. Keep JWT/database/Cloudinary settings in the hosting environment.
3. If enhanced Expo Push security is enabled for this EAS project, configure Expo__AccessToken on the backend with its authorized Expo access token. Never put that server token in the app. Otherwise it is optional.
4. Build and install a new APK; Android remote push is unavailable in Expo Go.
5. Sign in on the employee's phone, allow notifications, and check Account → Task notifications.
6. Verify a DevicePushTokens record exists, then assign a task from a manager login.
7. Check foreground/background delivery, notification tap navigation, denied permission, logout, and switching employees on one device.

The private Firebase service-account JSON must stay outside source control and app uploads. Patterns excluding service-account keys are included in the frontend .gitignore. google-services.json contains the Android app configuration and is included for EAS Build.

## Verification limits

Frontend tests use mocked Expo/HTTP dependencies; backend test fixtures use test doubles and do not send real employee messages. Build/type/lint/doctor/bundle results are reported separately. Backend execution has previously been blocked by Windows Application Control; a successful build does not mean the backend integration tests ran. Actual device delivery must be confirmed using the new APK and the configured Firebase/EAS project.
