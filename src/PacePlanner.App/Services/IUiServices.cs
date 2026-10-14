namespace PacePlanner.App.Services;

/// <summary>Window-bound UI interactions the view model needs (pickers and dialogs).</summary>
public interface IUiServices
{
    Task<string?> PickGpxFileAsync();

    Task<string?> PickSaveFileAsync(string suggestedName, string extension, string description);

    Task<string?> PromptTextAsync(string title, string initialText);

    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    void OpenWithShell(string path);
}
