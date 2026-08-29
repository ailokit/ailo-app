CREATE TABLE McpServers (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Transport INTEGER NOT NULL,
    Endpoint TEXT NULL,
    Command TEXT NULL,
    ArgumentsJson TEXT NOT NULL DEFAULT '[]',
    EnvironmentJson TEXT NOT NULL DEFAULT '{}',
    HeadersJson TEXT NOT NULL DEFAULT '{}',
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE McpTools (
    Id TEXT PRIMARY KEY,
    ServerId TEXT NOT NULL REFERENCES McpServers(Id) ON DELETE CASCADE,
    Name TEXT NOT NULL,
    Description TEXT NULL,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    UpdatedAt TEXT NOT NULL,
    UNIQUE (ServerId, Name)
);

CREATE INDEX IX_McpTools_ServerId ON McpTools(ServerId);
