using CommunityToolkit.Mvvm.ComponentModel;

namespace Ailo.ViewModels;

public sealed partial class AttachmentViewModel : ViewModelBase
{
    public AttachmentViewModel(string filePath, string fileName, string mimeType)
    {
        FilePath = filePath;
        FileName = fileName;
        MimeType = mimeType;
    }

    /// <summary>Absolute path of the copied file under the app attachments directory.</summary>
    public string FilePath { get; }

    /// <summary>Original file name, for display.</summary>
    public string FileName { get; }

    public string MimeType { get; }

    /// <summary>True once the attachment has been persisted with a sent message; only unsent copies may be deleted.</summary>
    [ObservableProperty] private bool _isPersisted;
}
