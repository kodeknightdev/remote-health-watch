-- One database and one login per service. Each login owns only its own database,
-- so a service can run its migrations but can't read anyone else's tables.
-- Passwords arrive as sqlcmd variables from the environment (see .env.example).
SET NOCOUNT ON;

DECLARE @services TABLE (name sysname, pwd nvarchar(128));
INSERT INTO @services VALUES
    (N'identity', N'$(IDENTITY_DB_PASSWORD)'),
    (N'patients', N'$(PATIENTS_DB_PASSWORD)'),
    (N'devices',  N'$(DEVICES_DB_PASSWORD)'),
    (N'vitals',   N'$(VITALS_DB_PASSWORD)'),
    (N'alerts',   N'$(ALERTS_DB_PASSWORD)'),
    (N'risk',     N'$(RISK_DB_PASSWORD)'),
    (N'insights', N'$(INSIGHTS_DB_PASSWORD)');

DECLARE @name sysname, @pwd nvarchar(128), @db sysname, @login sysname, @sql nvarchar(max);
DECLARE c CURSOR LOCAL FAST_FORWARD FOR SELECT name, pwd FROM @services;
OPEN c;
FETCH NEXT FROM c INTO @name, @pwd;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @db = N'rhw_' + @name;
    SET @login = N'rhw_' + @name;

    IF DB_ID(@db) IS NULL
    BEGIN
        SET @sql = N'CREATE DATABASE ' + QUOTENAME(@db) + N';';
        EXEC (@sql);
        -- snapshot isolation keeps readers off writers' locks on the busy vitals and alerts tables
        SET @sql = N'ALTER DATABASE ' + QUOTENAME(@db) + N' SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;';
        EXEC (@sql);
    END

    IF SUSER_ID(@login) IS NULL
        SET @sql = N'CREATE LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''') + N', CHECK_POLICY = OFF, DEFAULT_DATABASE = ' + QUOTENAME(@db) + N';';
    ELSE
        SET @sql = N'ALTER LOGIN ' + QUOTENAME(@login) + N' WITH PASSWORD = ' + QUOTENAME(@pwd, '''') + N';';
    EXEC (@sql);

    SET @sql = N'USE ' + QUOTENAME(@db) + N';
        IF USER_ID(' + QUOTENAME(@login, '''') + N') IS NULL CREATE USER ' + QUOTENAME(@login) + N' FOR LOGIN ' + QUOTENAME(@login) + N';
        ALTER ROLE db_owner ADD MEMBER ' + QUOTENAME(@login) + N';';
    EXEC (@sql);

    PRINT N'ready: ' + @db;
    FETCH NEXT FROM c INTO @name, @pwd;
END
CLOSE c;
DEALLOCATE c;
