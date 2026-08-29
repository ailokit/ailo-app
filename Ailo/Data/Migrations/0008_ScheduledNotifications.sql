CREATE TABLE ScheduledNotifications (
    Id TEXT PRIMARY KEY,
    CronExpression TEXT NOT NULL,
    Title TEXT NOT NULL,
    Subtitle TEXT NULL,
    Body TEXT NOT NULL,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    LastRunAtUtc TEXT NULL,
    NextRunAtUtc TEXT NOT NULL,
    CreatedAtUtc TEXT NOT NULL,
    UpdatedAtUtc TEXT NOT NULL
);

CREATE INDEX IX_ScheduledNotifications_NextRunAtUtc
    ON ScheduledNotifications(IsEnabled, NextRunAtUtc);
