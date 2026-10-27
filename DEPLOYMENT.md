# Deploy the employee access and attendance update

This backend returns employeeId and email in login/refresh/current-user responses and enforces employee ownership on the server. The frontend relies on these fields and on /api/employees/me and /api/employees/me/tasks.

Deploy origin/main to the Render service. After deployment, GET /api/auth/test must include apiVersion: employee-access-v2. Anonymous GET /api/employees and /api/attendance/office must return 401. If the version field is absent, the live service still runs older code.

appsettings.Production.json contains the office coordinates and a 500-metre radius. Hosting values Attendance__Latitude, Attendance__Longitude and Attendance__RadiusMeters override the file. Set Attendance__RadiusMeters to 500 if an older override exists. Keep connection strings, JWT key and Cloudinary credentials in the hosting environment, not Git.

The Neon role/attendance schema was applied separately using migration 20261027053449_AddRoleAccessAndAttendance. The older AddAttendance migration used a different data model and is superseded. This update uses EmployeeId, WorkDate and protected JPEG bytes in Attendance. Startup does not apply database migrations automatically. Do not reset the live database to fix a missing login-response field.

After backend deployment, distribute the matching frontend update and sign in again. Tasks cleared by the earlier requested reset remain empty until assigned by a manager.
