# Aadhaar last-four verification setup

This feature performs an application-level comparison only. It does not call UIDAI or
prove identity against UIDAI records. The manager enrolls the employee's full number;
the API encrypts it before saving and uses only a keyed hash of its last four digits
for the employee's comparison.

## Database

Run `aadhaar-verification-migration.sql` once in the same Neon database used by the
API. The table stores the encrypted full number, a keyed hash of its last four digits,
and verification state. Existing last-four-only rows remain unenrolled until the
manager enters the employee's full number.

## Secret key

The API needs a stable secret named `Aadhaar__DataKey`. Generate 32 random bytes in
PowerShell and copy the printed base64 value:

```powershell
$aadhaarKeyBytes = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Fill($aadhaarKeyBytes)
[Convert]::ToBase64String($aadhaarKeyBytes)
```

Set the generated value as a private environment variable in Render named
`Aadhaar__DataKey`. For local development, set the same-named environment variable
for the API process. Never commit the generated value or send it in chat. Keep it
unchanged: replacing it prevents the API from checking existing encrypted records,
so employees need to be re-enrolled.

## App flow

1. A manager enters the employee's full Aadhaar number only after confirming the employee agreed to this purpose.
2. The API encrypts the full number at rest and stores a keyed hash of its last four digits.
3. After signing in, the employee enters the last four digits on **My details**.
4. The API looks up the record through the signed-in employee ID, compares hashes, and stores verification state and time. It never returns the full number.
5. Aadhaar verification is optional; the employee can continue using the app without it.
