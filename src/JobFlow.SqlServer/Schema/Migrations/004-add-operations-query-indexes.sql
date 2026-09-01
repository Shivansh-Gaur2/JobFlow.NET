IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Jobs_Status_CreatedAt_Id'
      AND object_id = OBJECT_ID('dbo.Jobs')
)
BEGIN
    CREATE INDEX IX_Jobs_Status_CreatedAt_Id
        ON dbo.Jobs (Status, CreatedAt DESC, Id DESC);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Jobs_JobType_CreatedAt_Id'
      AND object_id = OBJECT_ID('dbo.Jobs')
)
BEGIN
    CREATE INDEX IX_Jobs_JobType_CreatedAt_Id
        ON dbo.Jobs (JobType, CreatedAt DESC, Id DESC);
END;

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_JobAttempts_WorkerId_JobId'
      AND object_id = OBJECT_ID('dbo.JobAttempts')
)
BEGIN
    CREATE INDEX IX_JobAttempts_WorkerId_JobId
        ON dbo.JobAttempts (WorkerId, JobId);
END;
