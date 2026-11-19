/*
  Least-privilege database user for the Taaj web app (SQL Server / Azure SQL).

  The app only reads and writes data. Changing tables (migrations) is done by the deployment with the
  owner/admin login, and the app runs with Database__MigrateOnStartup=false.

  Run once per database, connected to the Taaj database as the owner/admin.
  Replace the password (or, on Azure, prefer a managed identity: see docs/OPERATIONS.md).
*/

-- Azure SQL (contained user, no server login needed):
CREATE USER [taaj_app] WITH PASSWORD = N'<CHANGE-ME: long random password>';
-- SQL Server on a VM/VPS instead: CREATE LOGIN [taaj_app] WITH PASSWORD = N'...'; then CREATE USER [taaj_app] FOR LOGIN [taaj_app];

ALTER ROLE db_datareader ADD MEMBER [taaj_app];   -- SELECT on every table
ALTER ROLE db_datawriter ADD MEMBER [taaj_app];   -- INSERT / UPDATE / DELETE on every table

-- Order numbers come from a sequence (NEXT VALUE FOR needs UPDATE on it).
GRANT UPDATE ON OBJECT::dbo.OrderNumberSeq TO [taaj_app];

-- Not granted on purpose: db_owner, db_ddladmin (no CREATE/ALTER/DROP), ALTER ANY USER, CONTROL.
-- Check:  SELECT dp.name, r.name AS role FROM sys.database_role_members m
--         JOIN sys.database_principals r ON r.principal_id = m.role_principal_id
--         JOIN sys.database_principals dp ON dp.principal_id = m.member_principal_id WHERE dp.name = 'taaj_app';
