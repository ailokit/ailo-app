DROP INDEX IF EXISTS IX_CronJobs_NextRunAtUtc;

CREATE TABLE CronJobs_AutoIncrement (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    JobType TEXT NOT NULL,
    CronExpression TEXT NOT NULL,
    ParametersJson TEXT NOT NULL,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    LastRunAtUtc TEXT NULL,
    NextRunAtUtc TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

INSERT INTO CronJobs_AutoIncrement
    (JobType, CronExpression, ParametersJson, IsEnabled, LastRunAtUtc, NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc)
SELECT
    JobType, CronExpression, ParametersJson, IsEnabled, LastRunAtUtc, NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc
FROM CronJobs
ORDER BY rowid;

DROP TABLE CronJobs;
ALTER TABLE CronJobs_AutoIncrement RENAME TO CronJobs;

CREATE INDEX IX_CronJobs_NextRunAtUtc
    ON CronJobs(IsEnabled, NextRunAtUtc);
