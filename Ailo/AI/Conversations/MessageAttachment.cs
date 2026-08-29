namespace Ailo.AI.Conversations;

/// <summary>Metadata for an image attached to a user message. <see cref="FilePath"/> points to a copy stored under the app attachments directory.</summary>
public sealed record MessageAttachment(string FilePath, string FileName, string MimeType);
