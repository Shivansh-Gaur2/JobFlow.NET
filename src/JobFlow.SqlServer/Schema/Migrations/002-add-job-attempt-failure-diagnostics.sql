IF COL_LENGTH(N'dbo.JobAttempts', N'ErrorId') IS NULL
BEGIN
    ALTER TABLE dbo.JobAttempts
    ADD ErrorId UNIQUEIDENTIFIER NULL;
END

IF COL_LENGTH(N'dbo.JobAttempts', N'FailureType') IS NULL
BEGIN
    ALTER TABLE dbo.JobAttempts
    ADD FailureType NVARCHAR(200) NULL;
END

IF COL_LENGTH(N'dbo.JobAttempts', N'FailureMessage') IS NULL
BEGIN
    ALTER TABLE dbo.JobAttempts
    ADD FailureMessage NVARCHAR(1000) NULL;
END
