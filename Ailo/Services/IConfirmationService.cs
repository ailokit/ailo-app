namespace Ailo.Services;

public interface IConfirmationService
{
    Task<bool> ConfirmDeleteAsync(string itemName);

    /// <summary>Shows a deletion confirmation with an additional, prominent warning.</summary>
    Task<bool> ConfirmDeleteWithWarningAsync(string itemName, string warningMessage) => ConfirmDeleteAsync(itemName);
}
