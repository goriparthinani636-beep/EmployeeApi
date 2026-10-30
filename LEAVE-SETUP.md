# Leave requests

Employee: Leave tab > dates (YYYY-MM-DD), type and reason > Submit.
Manager: Leave tab > pending request > optional comment > Approve / Reject.
Employee receives a generic push notification and tapping it opens Leave history with the request highlighted. The actual status and private details are fetched from the authenticated API.

## Database and deployment
1. Run `leave-requests.sql` in Neon SQL Editor for the same database used by EmployeeApi. This adds one table and index; existing data is retained.
2. Deploy the updated EmployeeApi backend.
3. Build/install the updated frontend APK and allow notifications. Existing Firebase/EAS credentials are reused; no new private key is needed.
4. Sign in as an employee, submit a request, then sign in on a manager device to review it. Confirm the correct employee receives the update and can see the decision in Leave.

The EF migration `20261030070000_AddLeaveRequests` is included. Its CREATE statements are idempotent if SQL was already run. Run EF migrations later to record migration history; do not manually insert history entries.

## API
- GET /api/leaves: manager all, employee own only.
- POST /api/leaves: employee only; body `{ "startDate": "2026-11-02", "endDate": "2026-11-03", "leaveType": "Casual", "reason": "Family event" }`.
- PATCH /api/leaves/1/decision: manager only; body `{ "status": "Approved", "comment": "Approved" }`. Status can also be Rejected.

Leave types: Casual, Sick, Annual, Unpaid. New requests must start today or later using the Indian calendar date. Maximum 366 calendar days; weekends count. No leave balance, payroll deduction, holiday policy or attendance adjustment is implemented. Pending/approved overlapping requests are rejected. A manager decision is final in this version. Concurrent decisions are guarded by a conditional database update; overlap application checks use a serializable transaction.

Notification failure does not undo a saved decision. API returns notificationWarning. Expo ticket acceptance is not proof of phone delivery. Existing receipt worker removes invalid installations; pending receipts are in memory and lost on server restart. No durable resend queue is included. Leave reasons/dates/comments are never included in the push payload. Notification data contains request/account/employee IDs, scoped to the intended employee.
