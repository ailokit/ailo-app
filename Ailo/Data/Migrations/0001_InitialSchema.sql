CREATE TABLE ApiProviders (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    ProviderType INTEGER NOT NULL,
    ApiKey TEXT NOT NULL,
    Endpoint TEXT NULL,
    ModelId TEXT NOT NULL,
    IsDefault INTEGER NOT NULL DEFAULT 0,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE Skills (
    Id TEXT PRIMARY KEY,
    Name TEXT NOT NULL,
    Description TEXT NULL,
    SystemPrompt TEXT NOT NULL,
    Icon TEXT NULL,
    IsBuiltIn INTEGER NOT NULL DEFAULT 0,
    IsEnabled INTEGER NOT NULL DEFAULT 1,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    Version INTEGER NOT NULL DEFAULT 1,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE Conversations (
    Id TEXT PRIMARY KEY,
    Title TEXT NOT NULL,
    ProviderId TEXT NOT NULL REFERENCES ApiProviders(Id),
    SkillId TEXT NULL REFERENCES Skills(Id),
    ProviderConfiguration TEXT NOT NULL,
    SkillVersion INTEGER NULL,
    AgentType TEXT NOT NULL,
    AgentConfigurationHash TEXT NOT NULL,
    MafVersion TEXT NOT NULL,
    SessionState TEXT NOT NULL,
    SessionStatus INTEGER NOT NULL,
    IsArchived INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL
);

CREATE TABLE Messages (
    Id TEXT PRIMARY KEY,
    ConversationId TEXT NOT NULL REFERENCES Conversations(Id) ON DELETE CASCADE,
    SequenceNo INTEGER NOT NULL,
    Role INTEGER NOT NULL,
    Content TEXT NOT NULL,
    Status INTEGER NOT NULL,
    ErrorCode TEXT NULL,
    ErrorMessage TEXT NULL,
    CreatedAt TEXT NOT NULL,
    UpdatedAt TEXT NOT NULL,
    UNIQUE (ConversationId, SequenceNo)
);

CREATE TABLE AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);

CREATE INDEX IX_Conversations_UpdatedAt ON Conversations(UpdatedAt DESC);
CREATE INDEX IX_Messages_Conversation_Sequence ON Messages(ConversationId, SequenceNo);
CREATE INDEX IX_Skills_SortOrder ON Skills(SortOrder);

INSERT INTO Skills (Id, Name, Description, SystemPrompt, Icon, IsBuiltIn, IsEnabled, SortOrder, Version, CreatedAt, UpdatedAt) VALUES
('builtin-chat', 'General chat', 'A general-purpose AI assistant', 'You are Ailo, a helpful AI assistant.', '💬', 1, 1, 0, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
('builtin-translate', 'Translation', 'Natural and faithful translation', 'You are a professional translation assistant. Preserve the original meaning and produce natural, fluent translations.', '🌐', 1, 1, 1, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
('builtin-summarize', 'Summarization', 'Concise and accurate summaries', 'Summarize the input concisely and accurately.', '📝', 1, 1, 2, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
('builtin-code-explain', 'Code explanation', 'Explain code logic and design', 'Explain the code logic and key design decisions in clear, accessible language.', '💻', 1, 1, 3, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP),
('builtin-polish', 'Writing polish', 'Improve wording and expression', 'Improve the wording and correct grammar while preserving the original meaning.', '✍️', 1, 1, 4, 1, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP);
