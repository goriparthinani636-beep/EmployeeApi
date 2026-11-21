-- Run once in the target PostgreSQL database before deploying Aadhaar verification.
-- The full Aadhaar number is AES-GCM encrypted; the comparison value is a keyed hash of its last four digits.
CREATE TABLE IF NOT EXISTS "EmployeeAadhaarVerifications" (
    "EmployeeId" integer PRIMARY KEY,
    "EncryptedAadhaar" text NULL,
    "AadhaarNonce" text NULL,
    "AadhaarTag" text NULL,
    "PreEnrolledLastFourHash" character varying(64) NOT NULL,
    "IsVerified" boolean NOT NULL DEFAULT FALSE,
    "VerifiedAt" timestamp with time zone NULL,
    "FailedAttempts" integer NOT NULL DEFAULT 0,
    "IsLocked" boolean NOT NULL DEFAULT FALSE,
    CONSTRAINT "FK_EmployeeAadhaarVerifications_Employees_EmployeeId"
        FOREIGN KEY ("EmployeeId") REFERENCES "Employees" ("Id") ON DELETE CASCADE
);

-- These are safe to run when upgrading from the earlier last-four-only draft table.
ALTER TABLE "EmployeeAadhaarVerifications"
    ADD COLUMN IF NOT EXISTS "EncryptedAadhaar" text NULL;
ALTER TABLE "EmployeeAadhaarVerifications"
    ADD COLUMN IF NOT EXISTS "AadhaarNonce" text NULL;
ALTER TABLE "EmployeeAadhaarVerifications"
    ADD COLUMN IF NOT EXISTS "AadhaarTag" text NULL;
