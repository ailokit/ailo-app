using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ailo.Jobs;
using Ailo.Localization;
using Ailo.Logging;
using Ailo.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Ailo.ViewModels;

public sealed class ScheduledJobItem(CronJob job) : ObservableObject
{
    public int Id { get; } = job.Id;
    public string JobType { get; } = job.JobType;
    public string CronExpression { get; } = job.CronExpression;
    public string ParametersJson { get; } = FormatParameters(job.ParametersJson);
    public bool IsEnabled { get; } = job.IsEnabled;
    public bool IsOneTime { get; } = job.IsOneTime;
    public string NextRunAtText { get; } = job.NextRunAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string LastRunAtText { get; } = job.LastRunAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    private static string FormatParameters(string parametersJson)
    {
        try
        {
            using var document = JsonDocument.Parse(parametersJson);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, JsonWriterOptions))
            {
                document.RootElement.WriteTo(writer);
            }

            return DecodeUnicodeEscapes(Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (JsonException)
        {
            return parametersJson;
        }
    }

    private static readonly JsonWriterOptions JsonWriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = true
    };

    private static string DecodeUnicodeEscapes(string value)
    {
        var result = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            if (value[index] != '\\' || index + 1 >= value.Length)
            {
                result.Append(value[index]);
                continue;
            }

            // Preserve an escaped backslash so a literal "\\u1234" is not decoded.
            if (value[index + 1] == '\\')
            {
                result.Append("\\\\");
                index++;
                continue;
            }

            if (value[index + 1] != 'u' || index + 5 >= value.Length
                || !TryParseHex(value, index + 2, out var codePoint))
            {
                result.Append(value[index]);
                continue;
            }

            var character = (char)codePoint;
            if (char.IsHighSurrogate(character)
                && index + 11 < value.Length
                && value[index + 6] == '\\'
                && value[index + 7] == 'u'
                && TryParseHex(value, index + 8, out var lowCodePoint)
                && char.IsLowSurrogate((char)lowCodePoint))
            {
                result.Append(char.ConvertFromUtf32(char.ConvertToUtf32(character, (char)lowCodePoint)));
                index += 11;
            }
            else
            {
                result.Append(character);
                index += 5;
            }
        }

        return result.ToString();
    }

    private static bool TryParseHex(string value, int start, out int result)
    {
        result = 0;
        if (start + 4 > value.Length) return false;

        for (var index = start; index < start + 4; index++)
        {
            var digit = value[index] switch
            {
                >= '0' and <= '9' => value[index] - '0',
                >= 'a' and <= 'f' => value[index] - 'a' + 10,
                >= 'A' and <= 'F' => value[index] - 'A' + 10,
                _ => -1
            };
            if (digit < 0) return false;
            result = (result << 4) | digit;
        }

        return true;
    }
}

public sealed partial class ScheduledJobsSettingsViewModel : SettingsViewModelBase
{
    private readonly CronJobScheduler _scheduler;
    private readonly AppSettingsService _settings;
    private readonly IConfirmationService? _confirmation;
    private readonly SemaphoreSlim _runtimeSaveGate = new(1, 1);
    private bool _loadingSettings;
    private int _runtimeChangeVersion;

    public ScheduledJobsSettingsViewModel(
        CronJobScheduler scheduler,
        LocalizationService localization,
        AppSettingsService settings,
        IConfirmationService? confirmation = null)
        : base(localization)
    {
        _scheduler = scheduler;
        _settings = settings;
        _confirmation = confirmation;
    }

    public ObservableCollection<ScheduledJobItem> Jobs { get; } = [];

    [ObservableProperty] private ScheduledJobItem? _selectedJob;
    [ObservableProperty] private string _jobType = string.Empty;
    [ObservableProperty] private string _cronExpression = string.Empty;
    [ObservableProperty] private string _parametersJson = string.Empty;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private bool _isOneTime;
    [ObservableProperty] private string _maxJobRuntimeMinutes =
        ((int)AppSettingsService.DefaultJobMaxRuntime.TotalMinutes).ToString(CultureInfo.InvariantCulture);
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsJobDetailsVisible))]
    private bool _isJobSettingsVisible;

    public bool IsJobDetailsVisible => !IsJobSettingsVisible;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Jobs.Clear();
        foreach (var job in await _scheduler.GetAllAsync(cancellationToken).ConfigureAwait(false))
            Jobs.Add(new ScheduledJobItem(job));

        var maxRuntime = await _settings.GetJobMaxRuntimeAsync(cancellationToken).ConfigureAwait(false);
        _loadingSettings = true;
        try
        {
            MaxJobRuntimeMinutes = ((int)maxRuntime.TotalMinutes).ToString(CultureInfo.InvariantCulture);
            IsJobSettingsVisible = false;
        }
        finally
        {
            _loadingSettings = false;
        }
        SelectedJob = Jobs.FirstOrDefault();
    }

    [RelayCommand]
    private void ShowJobSettings() => IsJobSettingsVisible = true;

    [RelayCommand]
    private void ShowJobDetails() => IsJobSettingsVisible = false;

    [RelayCommand]
    private async Task SaveJobAsync()
    {
        try
        {
            var selectedJob = SelectedJob;
            if (selectedJob is null) return;

            if (string.IsNullOrWhiteSpace(CronExpression))
            {
                StatusMessage = T("JobCronRequired");
                return;
            }

            if (string.IsNullOrWhiteSpace(ParametersJson))
            {
                StatusMessage = T("JobParametersRequired");
                return;
            }

            var updated = await _scheduler.UpdateAsync(
                selectedJob.Id, CronExpression, ParametersJson, IsEnabled, isOneTime: IsOneTime).ConfigureAwait(false);
            if (updated is null) return;

            await LoadAsync().ConfigureAwait(false);
            SelectedJob = Jobs.FirstOrDefault(job => job.Id == updated.Id);
            StatusMessage = T("JobSaved");
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ScheduledJobsSettingsViewModel), "Failed to save scheduled job");
            StatusMessage = exception.Message;
        }
    }

    partial void OnMaxJobRuntimeMinutesChanged(string value)
    {
        if (_loadingSettings)
        {
            return;
        }

        var version = Interlocked.Increment(ref _runtimeChangeVersion);
        _ = PersistMaxJobRuntimeAsync(value, version);
    }

    private async Task PersistMaxJobRuntimeAsync(string value, int version)
    {
        if (!TryGetMaxJobRuntime(value, out var maxRuntime)) return;

        await _runtimeSaveGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // If the user typed another value while an earlier write was waiting, only persist the
            // latest value. Writes that already started are serialized before this one.
            if (version != Volatile.Read(ref _runtimeChangeVersion)) return;

            await _settings.SaveJobMaxRuntimeAsync(maxRuntime).ConfigureAwait(false);
            if (version == Volatile.Read(ref _runtimeChangeVersion))
            {
                StatusMessage = T("JobMaxRuntimeSaved");
            }
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ScheduledJobsSettingsViewModel), "Failed to save scheduled job settings");
            if (version == Volatile.Read(ref _runtimeChangeVersion))
            {
                StatusMessage = exception.Message;
            }
        }
        finally
        {
            _runtimeSaveGate.Release();
        }
    }

    private bool TryGetMaxJobRuntime(string value, out TimeSpan maxRuntime)
    {
        maxRuntime = default;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var maxRuntimeMinutes))
        {
            StatusMessage = T("JobMaxRuntimeInvalid");
            return false;
        }

        maxRuntime = TimeSpan.FromMinutes(maxRuntimeMinutes);
        try
        {
            AppSettingsService.ValidateJobMaxRuntime(maxRuntime);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            StatusMessage = T("JobMaxRuntimeInvalid");
            return false;
        }
    }

    [RelayCommand]
    private async Task DeleteJobAsync()
    {
        var selectedJob = SelectedJob;
        if (selectedJob is null) return;

        if (_confirmation is not null && !await _confirmation.ConfirmDeleteAsync(selectedJob.Id.ToString()).ConfigureAwait(false))
            return;

        try
        {
            if (await _scheduler.DeleteAsync(selectedJob.Id).ConfigureAwait(false))
            {
                await LoadAsync().ConfigureAwait(false);
                StatusMessage = T("JobDeleted");
            }
        }
        catch (Exception exception)
        {
            ExceptionLogger.Log(exception, nameof(ScheduledJobsSettingsViewModel), "Failed to delete scheduled job");
            StatusMessage = exception.Message;
        }
    }

    partial void OnSelectedJobChanged(ScheduledJobItem? value)
    {
        IsJobSettingsVisible = false;
        if (value is null)
        {
            JobType = string.Empty;
            CronExpression = string.Empty;
            ParametersJson = string.Empty;
            IsEnabled = false;
            IsOneTime = false;
            return;
        }

        JobType = value.JobType;
        CronExpression = value.CronExpression;
        ParametersJson = value.ParametersJson;
        IsEnabled = value.IsEnabled;
        IsOneTime = value.IsOneTime;
    }
}
