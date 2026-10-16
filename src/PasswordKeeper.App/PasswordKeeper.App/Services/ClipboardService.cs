using Windows.ApplicationModel.DataTransfer;

namespace PasswordKeeper.App.Services;

/// <summary>Copies secrets to the clipboard and clears them again after a delay.</summary>
public static class ClipboardService
{
    public static readonly TimeSpan ClearAfter = TimeSpan.FromSeconds(30);
    private static int _generation;

    public static async void CopySensitive(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();

        var mine = ++_generation;
        await Task.Delay(ClearAfter);
        if (mine != _generation) return; // a newer copy owns the clipboard now
        try
        {
            // Only clear if the clipboard still holds what we put there.
            var current = await Clipboard.GetContent().GetTextAsync();
            if (current == text) Clipboard.Clear();
        }
        catch
        {
            // Clipboard may be unavailable (app in background); nothing more we can do.
        }
    }
}
