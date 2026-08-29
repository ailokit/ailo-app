CREATE TABLE ProviderModels (
    ProviderId TEXT NOT NULL REFERENCES ApiProviders(Id) ON DELETE CASCADE,
    ModelId TEXT NOT NULL,
    PRIMARY KEY (ProviderId, ModelId)
);

INSERT INTO ProviderModels (ProviderId, ModelId)
SELECT Id, ModelId FROM ApiProviders;
