IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Jobs')
BEGIN
    CREATE TABLE Jobs (
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        JobType NVARCHAR(255) NOT NULL,
        Payload NVARCHAR(MAX) NULL,
        Status TINYINT NOT NULL,
        NextRunAt DATETIMEOFFSET NOT NULL,
        CreatedAt DATETIMEOFFSET NOT NULL,
        RetryCount INT NOT NULL DEFAULT 0,
        MaxRetries INT NOT NULL DEFAULT 3,
        LockedBy NVARCHAR(100) NULL,
        LockedAt DATETIMEOFFSET NULL
    );

    CREATE INDEX IX_Jobs_Status_NextRunAt ON Jobs (Status, NextRunAt);
END