# WhatsApp task and leave notifications

The API sends approved WhatsApp template messages after a task is assigned (including reassignment) and after a manager approves or rejects a leave request. Existing push notifications continue to work independently.

## Safety defaults

- WhatsApp sending is off unless `WhatsApp__Enabled=true` is configured on the API host.
- Each employee must have a phone number and must have agreed to WhatsApp notifications. The manager form checkbox records that confirmation; it should only be enabled after the employee opted in.
- The Meta access token is read only by the .NET API. Never put it in the React Native app, source control, screenshots, or chat.
- Task and leave messages use approved WhatsApp templates. Leave reasons and manager comments are not sent.
- Existing employees are opted out by default after the database migration.

## Neon schema change

Run `whatsapp-opt-in-migration.sql` once in the Neon SQL Editor before deploying the updated API. It adds the opt-in field with `false` for all existing employees.

## Meta setup

For a demo, begin with the Meta-provided test sender and a recipient number you control. The Meta test sender is not your own WhatsApp number. To send from your own number later, complete the WhatsApp Business Platform phone-number setup for a number you intend to use as the sender. Do not register a personal number unless you understand the effect on its existing WhatsApp use.

Create and get approval for two Utility templates in WhatsApp Manager. The body placeholders must match the API parameters:

- `employee_task_assigned` (`en_US`): `Hi {{1}}, a task "{{2}}" has been assigned to you. Open EmployeeApp to view details.`
- `employee_leave_decision` (`en_US`): `Hi {{1}}, your leave request for {{2}} was {{3}}. Open EmployeeApp to view details.`

Employees must agree to receive these transactional WhatsApp updates. Meta's template review and messaging policies apply.

## Render environment variables

Add these to the **EmployeeApi** service environment, not the frontend or APK:

| Key | Value |
| --- | --- |
| `WhatsApp__Enabled` | `true` only after setup is complete |
| `WhatsApp__AccessToken` | Meta access token; keep secret |
| `WhatsApp__PhoneNumberId` | Sender's phone number ID from Meta |
| `WhatsApp__GraphApiVersion` | Current Graph API version supported by the app, e.g. `v27.0` |
| `WhatsApp__TaskAssignedTemplateName` | `employee_task_assigned` |
| `WhatsApp__LeaveDecisionTemplateName` | `employee_leave_decision` |
| `WhatsApp__LanguageCode` | `en_US` |
| `WhatsApp__DefaultCountryCode` | `91` for local 10-digit Indian employee numbers |

After setting credentials, deploy the API. A response includes `whatsAppNotification` as `sent`, `skipped`, or `failed`; sending failures do not undo the saved task or leave decision.
