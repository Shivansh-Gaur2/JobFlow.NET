IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_Jobs_Status_LeaseExpiresAt'
      AND object_id = OBJECT_ID('dbo.Jobs')
)
BEGIN
    CREATE INDEX IX_Jobs_Status_LeaseExpiresAt
        ON dbo.Jobs (Status, LeaseExpiresAt);
END;
