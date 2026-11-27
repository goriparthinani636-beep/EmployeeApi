CREATE TABLE IF NOT EXISTS "CompanyProfiles" (
    "Id" integer PRIMARY KEY CHECK ("Id" = 1),
    "CompanyName" character varying(120) NOT NULL,
    "ManagerName" character varying(120) NOT NULL,
    "ManagerEmail" character varying(256) NOT NULL,
    "ManagerPhone" character varying(30),
    "UpdatedAt" timestamp with time zone NOT NULL,
    "UpdatedByUserId" integer NOT NULL REFERENCES "Users"("Id") ON DELETE RESTRICT
);
