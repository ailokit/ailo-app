# Ailo

> Ailo — One Call, Intelligence Arrives.

Ailo is a native desktop AI workspace built with .NET 10 and Avalonia. It brings models, conversations, tools, workspaces, and scheduled tasks into one lightweight, controlled application. Use it as a fast chat entry point or explicitly authorize it to work with files, webpages, notifications, and recurring tasks.

## Features

- Native .NET 10 and Avalonia desktop experience with a tray icon, global hotkeys, and light or dark themes.
- OpenAI, Ollama, and OpenAI-compatible providers, with support for both local and hosted models.
- Markdown, code blocks, images, clipboard attachments, and local SQLite conversation history.
- Workspace boundaries that restrict file access and prevent path traversal or symlink escapes.
- Optional webpage, system information, notification, workspace file, and MCP tools.
- Persistent Cron-based agent jobs with local-shell execution confined to their working directory.
- English, Simplified Chinese, and Traditional Chinese user interfaces.

## Getting started

### Requirements

- .NET SDK 10.0 or later.
- A desktop environment supported by Avalonia.
- Provider credentials or a locally running Ollama instance.

```bash
dotnet restore Ailo.slnx
dotnet run --project Ailo/Ailo.csproj
```

After launching Ailo, add a model provider in Settings, then select a model and skill. To work with local files, select files or folders as the chat workspace before sending a request.

## Data and privacy

- API keys are stored locally and sent only to the provider endpoint configured by the user.
- Conversations, messages, providers, skills, attachments, exports, and logs are stored in the local data directory.
- The default data directory is selected by the operating system. A custom directory can be selected in General Settings.
- On the first launch after upgrading, Ailo automatically discovers legacy application data, copies missing files, and renames the legacy SQLite database and sidecar files to the Ailo names. Legacy directories are preserved for backup or rollback.
- Versioned SQLite migrations upgrade the database schema automatically; no manual import is required.

## Scheduled job safety

Scheduled agents are intended for recurring reports, checks, and project maintenance. Each job has an explicit Cron expression, prompt, and working directory.

- Shell access for scheduled agents uses the local shell and is confined to the working directory. If no directory is supplied, Ailo selects the configured default workspace when the job runs.
- Scheduled jobs can be marked as one-time tasks; they are removed automatically after execution. The default is recurring.
- Scheduled agents cannot ask interactive questions.
- Job progress and errors are appended to `ailo-agent-job-<id>.log` in the working directory.

## Build and test

```bash
dotnet build Ailo.slnx
dotnet test Ailo.slnx --no-build
```

Release build:

```bash
dotnet build Ailo.slnx --configuration Release
```

Supported release targets:

- `win-x64`
- `win-arm64`
- `osx-x64`
- `osx-arm64`

## Project structure

```text
Ailo/
├── AI/            Agent orchestration, skills, tools, conversations, and model calls
├── Data/          SQLite access, repositories, and database migrations
├── Jobs/          Cron scheduling and unattended agent execution
├── Services/      Settings, logging, localization, notifications, and platform services
├── ViewModels/    MVVM presentation logic
└── Views/         Avalonia windows and settings pages
```

## License

See [LICENSE](LICENSE).
