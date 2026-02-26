IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

CREATE TABLE [Projects] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [Description] nvarchar(max) NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NOT NULL,
    [ScanSource] int NOT NULL,
    CONSTRAINT [PK_Projects] PRIMARY KEY ([Id])
);
GO

CREATE TABLE [ProjectRuns] (
    [Id] int NOT NULL IDENTITY,
    [ProjectId] int NOT NULL,
    [CreatedAtUtc] datetime2 NOT NULL,
    [CreatedBy] nvarchar(max) NOT NULL,
    [Status] int NOT NULL,
    [Error] nvarchar(max) NULL,
    [MaxConcurrency] int NOT NULL,
    [AllowReboot] bit NOT NULL,
    [OnlineOnly] bit NOT NULL,
    [ErrorPolicy] nvarchar(max) NULL,
    [ScheduledForUtc] datetime2 NULL,
    [HangfireJobId] nvarchar(max) NULL,
    CONSTRAINT [PK_ProjectRuns] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProjectRuns_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [ProjectServers] (
    [Id] int NOT NULL IDENTITY,
    [ProjectId] int NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Fqdn] nvarchar(max) NULL,
    [Environment] nvarchar(max) NULL,
    [PreFlightStatus] int NOT NULL,
    [PreFlightError] nvarchar(max) NULL,
    [LastScanAtUtc] datetime2 NULL,
    CONSTRAINT [PK_ProjectServers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProjectServers_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [ProjectRunServers] (
    [Id] int NOT NULL IDENTITY,
    [ProjectRunId] int NOT NULL,
    [ProjectServerId] int NOT NULL,
    [Status] int NOT NULL,
    [LastUpdatedUtc] datetime2 NULL,
    [LastError] nvarchar(max) NULL,
    [LogPath] nvarchar(max) NULL,
    [LogTail] nvarchar(max) NULL,
    CONSTRAINT [PK_ProjectRunServers] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ProjectRunServers_ProjectRuns_ProjectRunId] FOREIGN KEY ([ProjectRunId]) REFERENCES [ProjectRuns] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_ProjectRunServers_ProjectServers_ProjectServerId] FOREIGN KEY ([ProjectServerId]) REFERENCES [ProjectServers] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [ServerScanResults] (
    [Id] int NOT NULL IDENTITY,
    [ProjectId] int NOT NULL,
    [ProjectServerId] int NOT NULL,
    [ScanTimeUtc] datetime2 NOT NULL,
    [ScanSource] int NOT NULL,
    [RawOutput] nvarchar(max) NULL,
    CONSTRAINT [PK_ServerScanResults] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ServerScanResults_ProjectServers_ProjectServerId] FOREIGN KEY ([ProjectServerId]) REFERENCES [ProjectServers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ServerScanResults_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [ServerSelections] (
    [Id] int NOT NULL IDENTITY,
    [ProjectId] int NOT NULL,
    [ProjectServerId] int NOT NULL,
    [KbNumber] nvarchar(max) NOT NULL,
    [Title] nvarchar(max) NOT NULL,
    [Category] nvarchar(max) NULL,
    CONSTRAINT [PK_ServerSelections] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_ServerSelections_ProjectServers_ProjectServerId] FOREIGN KEY ([ProjectServerId]) REFERENCES [ProjectServers] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_ServerSelections_Projects_ProjectId] FOREIGN KEY ([ProjectId]) REFERENCES [Projects] ([Id]) ON DELETE NO ACTION
);
GO

CREATE TABLE [UpdateCandidates] (
    [Id] int NOT NULL IDENTITY,
    [ServerScanResultId] int NOT NULL,
    [KbNumber] nvarchar(max) NOT NULL,
    [Title] nvarchar(max) NOT NULL,
    [Category] nvarchar(max) NULL,
    [Severity] nvarchar(max) NULL,
    [IsSelectedByDefault] bit NOT NULL,
    CONSTRAINT [PK_UpdateCandidates] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_UpdateCandidates_ServerScanResults_ServerScanResultId] FOREIGN KEY ([ServerScanResultId]) REFERENCES [ServerScanResults] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_ProjectRuns_ProjectId] ON [ProjectRuns] ([ProjectId]);
GO

CREATE UNIQUE INDEX [IX_ProjectRunServers_ProjectRunId_ProjectServerId] ON [ProjectRunServers] ([ProjectRunId], [ProjectServerId]);
GO

CREATE INDEX [IX_ProjectRunServers_ProjectServerId] ON [ProjectRunServers] ([ProjectServerId]);
GO

CREATE INDEX [IX_ProjectServers_ProjectId] ON [ProjectServers] ([ProjectId]);
GO

CREATE INDEX [IX_ServerScanResults_ProjectId] ON [ServerScanResults] ([ProjectId]);
GO

CREATE INDEX [IX_ServerScanResults_ProjectServerId] ON [ServerScanResults] ([ProjectServerId]);
GO

CREATE INDEX [IX_ServerSelections_ProjectId] ON [ServerSelections] ([ProjectId]);
GO

CREATE INDEX [IX_ServerSelections_ProjectServerId] ON [ServerSelections] ([ProjectServerId]);
GO

CREATE INDEX [IX_UpdateCandidates_ServerScanResultId] ON [UpdateCandidates] ([ServerScanResultId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260226170707_InitialCreate', N'8.0.0');
GO

COMMIT;
GO

