IF OBJECT_ID(N'dbo.JobUpdates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.JobUpdates
    (
        Id UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_JobUpdates PRIMARY KEY,
        JobId UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_JobUpdates_Jobs
            FOREIGN KEY REFERENCES dbo.Jobs(Id),
        OccurredAt DATETIMEOFFSET NOT NULL,
        PublishedAt DATETIMEOFFSET NULL
    );

    CREATE INDEX IX_JobUpdates_Unpublished
        ON dbo.JobUpdates (PublishedAt, OccurredAt)
        WHERE PublishedAt IS NULL;
END;
