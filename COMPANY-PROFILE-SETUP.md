# Company profile setup

This is a single-company profile for the current practice app. Managers can edit the company name and manager contact. Employees can see the company name in the app header; manager contact details remain manager-only.

## First-time database setup

1. In Neon, open the SQL editor for the database used by the API.
2. Run `company-profile-setup.sql` once. The script only creates the new `CompanyProfiles` table if it does not already exist; it does not change existing employee or login data.
3. Deploy the updated `EmployeeApiDeploy` backend to Render.
4. Build and install the updated `EmployeeApp`.
5. Sign in as Manager. Open **Account → Company and manager details**, enter the details, and save. The dashboard and app header then show the company name; the dashboard also shows the manager contact.

The API allows managers to read and update the profile. Employee accounts can read only the company name. The profile row uses fixed ID `1` because the current app has one shared workspace and no organization/tenant model yet.
