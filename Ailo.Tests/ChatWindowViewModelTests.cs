using Ailo.AI;
using Ailo.AI.Conversations;
using Ailo.AI.Providers;
using Ailo.AI.Skills;
using Ailo.AI.Tools;
using Ailo.Data;
using Ailo.Services;
using Ailo.ViewModels;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Ailo.Tests;

public sealed class ChatWindowViewModelTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.db");

    [Fact]
    public async Task PrepareNewSession_SelectsDefaultProviderAndFirstSkill()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        viewModel.AppState.Providers.Add(CreateProvider());
        viewModel.AppState.Skills.Add(CreateSkill());

        viewModel.PrepareNewSession();

        Assert.Equal("provider", viewModel.SelectedProvider?.Id);
        Assert.Equal("builtin-chat", viewModel.SelectedSkill?.Id);
    }

    [Fact]
    public async Task ReloadingSelectedSkill_UsesTheUpdatedPromptObject()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        var original = CreateSkill();
        viewModel.AppState.Skills.Add(original);
        viewModel.PrepareNewSession();

        var updated = original with { SystemPrompt = "Answer only in Spanish.", Version = original.Version + 1 };
        viewModel.AppState.Skills.Clear();
        viewModel.AppState.Skills.Add(updated);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(updated, viewModel.SelectedSkill);
        Assert.Equal(updated.SystemPrompt, viewModel.SelectedSkill?.SystemPrompt);
    }

    [Fact]
    public async Task SelectedProvider_ExposesAllModelsAndSelectsTheActiveModel()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        viewModel.AppState.Providers.Add(CreateProvider(modelIds: ["model-a", "model-b"]));

        viewModel.PrepareNewSession();

        Assert.Equal(["model-a", "model-b"], viewModel.AvailableModels);
        Assert.Equal("model-a", viewModel.SelectedModelId);
    }

    [Fact]
    public async Task SelectingModel_UpdatesSelectionAndDisplayNames()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        viewModel.AppState.Providers.Add(CreateProvider(modelIds: ["model-a", "model-b"]));
        viewModel.PrepareNewSession();

        viewModel.SelectedModelId = "model-b";

        Assert.Equal("provider", viewModel.SelectedProvider?.Id);
        Assert.Equal("model-b", viewModel.SelectedModelId);
        Assert.Equal("Default", viewModel.SelectedProviderDisplayName);
        Assert.Equal("model-b", viewModel.SelectedModelDisplayName);
    }

    [Fact]
    public void NavigationCommands_OpenCorrespondingWorkspaceWindows()
    {
        var database = new SqliteDatabase(_path);
        var navigation = new RecordingNavigation();
        var viewModel = CreateViewModel(database, navigation);

        viewModel.OpenSettingsCommand.Execute(null);
        viewModel.OpenSkillWorkbenchCommand.Execute(null);

        Assert.Equal(1, navigation.SettingsCount);
        Assert.Equal(1, navigation.SkillSettingsCount);
    }

    [Fact]
    public async Task ShowAddAttachmentButton_TracksSelectedModelMultimodalFlag()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        var provider = CreateProvider(modelIds: ["model-a", "model-b"]) with
        {
            MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
        };
        viewModel.AppState.Providers.Add(provider);

        viewModel.PrepareNewSession();

        Assert.False(viewModel.ShowAddAttachmentButton);
        viewModel.SelectedModelId = "model-b";
        Assert.True(viewModel.ShowAddAttachmentButton);
        viewModel.SelectedModelId = "model-a";
        Assert.False(viewModel.ShowAddAttachmentButton);
    }

    [Fact]
    public async Task AddAttachmentsAsync_CopiesFilesAndExposesThumbnails()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var attachmentsRoot = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}");
        var viewModel = CreateViewModel(database, appPaths: new AppPaths(attachmentsRoot));
        var source = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(source, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        await viewModel.AddAttachmentsAsync([source]);

        var attachment = Assert.Single(viewModel.Attachments);
        Assert.Equal("image/png", attachment.MimeType);
        Assert.Equal(Path.GetFileName(source), attachment.FileName);
        Assert.True(File.Exists(attachment.FilePath));
        Assert.StartsWith(new AppPaths(attachmentsRoot).AttachmentsDirectory, attachment.FilePath);
        File.Delete(source);
    }

    [Fact]
    public async Task AddClipboardImageAsync_SavesPngAndExposesThumbnail()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var attachmentsRoot = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}");
        var viewModel = CreateViewModel(database, appPaths: new AppPaths(attachmentsRoot));
        var image = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        await viewModel.AddClipboardImageAsync(new MemoryStream(image));

        var attachment = Assert.Single(viewModel.Attachments);
        Assert.Equal("clipboard.png", attachment.FileName);
        Assert.Equal("image/png", attachment.MimeType);
        Assert.Equal(image, File.ReadAllBytes(attachment.FilePath));
    }

    [Fact]
    public async Task RemoveAttachment_DeletesUnsentCopiedFile()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var attachmentsRoot = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}");
        var viewModel = CreateViewModel(database, appPaths: new AppPaths(attachmentsRoot));
        var source = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(source, [0xFF, 0xD8, 0xFF]);
        await viewModel.AddAttachmentsAsync([source]);

        viewModel.RemoveAttachmentCommand.Execute(viewModel.Attachments[0]);

        Assert.Empty(viewModel.Attachments);
        Assert.DoesNotContain(Directory.GetFiles(new AppPaths(attachmentsRoot).AttachmentsDirectory), static f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
        File.Delete(source);
    }

    [Fact]
    public async Task SwitchingToNonMultimodalModel_ClearsAttachments()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        var provider = CreateProvider(modelIds: ["model-a", "model-b"]) with
        {
            MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
        };
        viewModel.AppState.Providers.Add(provider);
        viewModel.PrepareNewSession();
        viewModel.SelectedModelId = "model-b";
        await viewModel.AddAttachmentsAsync([CreateTempImage(out _)]);

        viewModel.SelectedModelId = "model-a";

        Assert.Empty(viewModel.Attachments);
    }

    [Fact]
    public async Task CanSend_TrueWithOnlyAttachments()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var viewModel = CreateViewModel(database);
        viewModel.AppState.Providers.Add(CreateProvider(modelIds: ["model-b"]) with
        {
            MultimodalModelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "model-b" }
        });
        viewModel.PrepareNewSession();
        viewModel.SelectedModelId = "model-b";
        await viewModel.AddAttachmentsAsync([CreateTempImage(out _)]);

        Assert.True(viewModel.SendCommand.CanExecute(null));
    }

    [Fact]
    public async Task WorkspaceSelection_IsScopedToCurrentChatAndClearedForNewConversation()
    {
        var database = new SqliteDatabase(_path);
        await new DatabaseMigrator(database).MigrateAsync();
        var workspace = new ChatWorkspace();
        var viewModel = CreateViewModel(database, workspace: workspace);
        var folder = Path.Combine(Path.GetTempPath(), "Ailo.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            viewModel.AddWorkspaceEntries([new WorkspaceEntry(folder, IsDirectory: true)]);

            Assert.True(viewModel.HasWorkspaceEntries);
            Assert.Single(workspace.Entries);

            viewModel.NewConversationCommand.Execute(null);

            Assert.False(viewModel.HasWorkspaceEntries);
            Assert.Empty(workspace.Entries);
        }
        finally
        {
            Directory.Delete(folder);
        }
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }

    private static ChatWindowViewModel CreateViewModel(SqliteDatabase database, IWindowNavigationService? navigation = null, AppPaths? appPaths = null, ChatWorkspace? workspace = null)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var appState = new AppState(new LazyServiceProvider(services));
        var chatWindowManager = new ChatWindowManager(services.GetRequiredService<IServiceScopeFactory>(), appState);
        workspace ??= new ChatWorkspace();
        return new ChatWindowViewModel(
            new ConversationService(new ConversationRepository(database)),
            new ChatService(new MessageRepository(database), new ConversationRepository(database), new ApiProviderRepository(database), new SessionRunLock(), new ChatToolRegistry([]), workspace),
            new ConversationRepository(database),
            new MessageRepository(database),
            appState,
            chatWindowManager,
            workspace,
            navigation,
            appPaths: appPaths ?? new AppPaths(Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}")));
    }

    private static ApiProvider CreateProvider(string[]? modelIds = null) => new(
        "provider", "Default", ProviderType.OpenAi, "key", null, "model-a", true, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)
    {
        ModelIds = modelIds ?? ["model-a"]
    };

    private static Skill CreateSkill() => new(
        "builtin-chat", "General chat", null, "You are Ailo, a helpful AI assistant.", "💬", true, true, 0, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static string CreateTempImage(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), "Ailo.Tests", $"{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        return path;
    }

    private sealed class RecordingNavigation : IWindowNavigationService
    {
        public int SettingsCount { get; private set; }
        public int SkillSettingsCount { get; private set; }

        public void ShowSettings() => SettingsCount++;
        public void ShowSkillSettings() => SkillSettingsCount++;
    }
}
