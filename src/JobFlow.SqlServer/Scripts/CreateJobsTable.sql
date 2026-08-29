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
        LockedAt DATETIMEOFFSET NULL,
        LeaseToken UNIQUEIDENTIFIER NULL,
        LeaseExpiresAt DATETIMEOFFSET NULL
    );

    CREATE TABLE dbo.JobAttempts
    (
        Id UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT PK_JobAttempts PRIMARY KEY,

        JobId UNIQUEIDENTIFIER NOT NULL
            CONSTRAINT FK_JobAttempts_Jobs
            FOREIGN KEY REFERENCES dbo.Jobs(Id),

        AttemptNumber INT NOT NULL,

        WorkerId NVARCHAR(200) NOT NULL,
        LeaseToken UNIQUEIDENTIFIER NOT NULL,

        Status NVARCHAR(32) NOT NULL,
        StartedAt DATETIMEOFFSET NOT NULL,
        FinishedAt DATETIMEOFFSET NULL,

        CONSTRAINT CK_JobAttempts_Status
            CHECK (Status IN ('Running', 'Completed', 'Failed', 'Abandoned')),

        CONSTRAINT UQ_JobAttempts_Job_AttemptNumber
            UNIQUE (JobId, AttemptNumber)
    );

    CREATE INDEX IX_JobAttempts_JobId_StartedAt
        ON dbo.JobAttempts (JobId, StartedAt);

    CREATE INDEX IX_Jobs_Status_NextRunAt ON Jobs (Status, NextRunAt);
END

IF COL_LENGTH(N'dbo.Jobs', N'LeaseToken') IS NULL
BEGIN
    ALTER TABLE dbo.Jobs
    ADD LeaseToken UNIQUEIDENTIFIER NULL;
END

IF COL_LENGTH(N'dbo.Jobs', N'LeaseExpiresAt') IS NULL
BEGIN
    ALTER TABLE dbo.Jobs
    ADD LeaseExpiresAt DATETIMEOFFSET NULL;
END
