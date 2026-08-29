CREATE TABLE CronJobs (
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

CREATE INDEX IX_CronJobs_NextRunAtUtc
    ON CronJobs(IsEnabled, NextRunAtUtc);

INSERT OR IGNORE INTO CronJobs
    (JobType, CronExpression, ParametersJson, IsEnabled, LastRunAtUtc, NextRunAtUtc, CreatedAtUtc, UpdatedAtUtc)
SELECT
    'native_notification',
    CronExpression,
    json_object('Title', Title, 'Body', Body, 'Subtitle', Subtitle),
    IsEnabled,
    LastRunAtUtc,
    NextRunAtUtc,
    CreatedAtUtc,
    UpdatedAtUtc
FROM ScheduledNotifications;

DROP TABLE ScheduledNotifications;
