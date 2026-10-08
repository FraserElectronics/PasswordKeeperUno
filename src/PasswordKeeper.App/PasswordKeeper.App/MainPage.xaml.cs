using PasswordKeeper.App.Services;
using PasswordKeeper.Core.Generation;
using PasswordKeeper.Core.Vault;
using Windows.Storage.Pickers;
using Windows.System;

namespace PasswordKeeper.App;

public sealed partial class MainPage : Page
{
    private const int MinMasterLength = 12;
    private static readonly TimeSpan IdleLockAfter = TimeSpan.FromMinutes(5);

    private VaultSession _session = new(VaultSession.DefaultPath());
    private readonly DispatcherTimer _idleTimer = new() { Interval = IdleLockAfter };
    private VaultEntry? _current;
    private bool _loadingEditor;

    public MainPage()
    {
        this.InitializeComponent();

        _idleTimer.Tick += (_, _) => LockVault();
        // Any interaction postpones the auto-lock.
        AddHandler(PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => ResetIdle()), true);
        AddHandler(KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, _) => ResetIdle()), true);

        ShowUnlock();
        // Without explicit focus the first keystrokes have no target, which makes macOS beep.
        Loaded += (_, _) => MasterBox.Focus(FocusState.Programmatic);
    }

    // ---- Unlock / lock -------------------------------------------------------------------

    private void ShowUnlock()
    {
        VaultPanel.Visibility = Visibility.Collapsed;
        UnlockPanel.Visibility = Visibility.Visible;
        MasterBox.Password = "";
        ConfirmBox.Password = "";
        UnlockError.Text = "";

        PathText.Text = _session.Path;
        var creating = !_session.FileExists;
        ConfirmBox.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        UnlockButton.Content = creating ? "Create vault" : "Unlock";
        UnlockHint.Text = creating
            ? $"No vault found. Choose a master password of at least {MinMasterLength} characters. " +
              "It cannot be recovered if you forget it."
            : "Enter your master password.";
        MasterBox.Focus(FocusState.Programmatic);
    }

    private void OnShowMasterChanged(object sender, RoutedEventArgs e)
    {
        var mode = ShowMasterCheck.IsChecked == true ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
        MasterBox.PasswordRevealMode = mode;
        ConfirmBox.PasswordRevealMode = mode;
    }

    private void OnMasterChanged(object sender, RoutedEventArgs e)
    {
        if (ConfirmBox.Visibility != Visibility.Visible) { MatchText.Text = ""; return; }
        var a = MasterBox.Password;
        var b = ConfirmBox.Password;
        MatchText.Text = $"{a.Length} / {b.Length} characters. " +
                         (b.Length == 0 ? "" : a == b ? "Match." : "Not matching yet.");
    }

    private async void OnOpenVaultClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".pkv");
        var file = await picker.PickSingleFileAsync();
        if (file is not null) UseVaultPath(file.Path);
    }

    private async void OnNewVaultClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker { SuggestedFileName = "vault" };
        picker.FileTypeChoices.Add("PasswordKeeper vault", new List<string> { ".pkv" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        if (new FileInfo(file.Path).Exists && new FileInfo(file.Path).Length > 0)
        {
            UnlockError.Text = "That file already contains data. Use \"Open vault file...\" to open it.";
            return;
        }
        UseVaultPath(file.Path);
    }

    private void UseVaultPath(string path)
    {
        new AppSettings { VaultPath = path }.Save();
        _session = new VaultSession(path);
        ShowUnlock();
    }

    private void OnMasterKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) OnUnlockClick(sender, e);
    }

    private async void OnUnlockClick(object sender, RoutedEventArgs e)
    {
        UnlockError.Text = "";
        var pw = MasterBox.Password;
        UnlockButton.IsEnabled = false;
        try
        {
            if (!_session.FileExists)
            {
                if (pw.Length < MinMasterLength)
                    throw new InvalidOperationException($"Use at least {MinMasterLength} characters.");
                if (pw != ConfirmBox.Password)
                    throw new InvalidOperationException("The two passwords don't match.");
                await _session.CreateAsync(pw);
            }
            else
            {
                await _session.UnlockAsync(pw);
            }
            ShowVault();
        }
        catch (VaultAuthenticationException ex) { UnlockError.Text = ex.Message; }
        catch (InvalidVaultFormatException ex) { UnlockError.Text = ex.Message; }
        catch (InvalidOperationException ex) { UnlockError.Text = ex.Message; }
        finally
        {
            MasterBox.Password = "";
            ConfirmBox.Password = "";
            UnlockButton.IsEnabled = true;
        }
    }

    private void OnLockClick(object sender, RoutedEventArgs e) => LockVault();

    private void LockVault()
    {
        _idleTimer.Stop();
        _session.Lock();
        _current = null;
        ClearEditor();
        EntryList.ItemsSource = null;
        SearchBox.Text = "";
        ShowUnlock();
    }

    private void ResetIdle()
    {
        if (!_session.IsUnlocked) return;
        _idleTimer.Stop();
        _idleTimer.Start();
    }

    // ---- Vault list ----------------------------------------------------------------------

    private void ShowVault()
    {
        UnlockPanel.Visibility = Visibility.Collapsed;
        VaultPanel.Visibility = Visibility.Visible;
        RefreshList();
        ClearEditor();
        ResetIdle();
    }

    private void RefreshList(Guid? select = null)
    {
        var q = SearchBox.Text.Trim();
        var items = _session.Data!.Entries
            .Where(en => q.Length == 0 ||
                         en.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         en.Url.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                         en.Username.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(en => en.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        EntryList.ItemsSource = items;
        if (select is { } id) EntryList.SelectedItem = items.FirstOrDefault(en => en.Id == id);
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (_session.IsUnlocked) RefreshList(_current?.Id);
    }

    private void OnEntrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (EntryList.SelectedItem is VaultEntry entry) LoadEditor(entry);
    }

    // ---- Editor --------------------------------------------------------------------------

    private void LoadEditor(VaultEntry entry)
    {
        _loadingEditor = true;
        _current = entry;
        TitleBox.Text = entry.Title;
        UrlBox.Text = entry.Url;
        UserBox.Text = entry.Username;
        PassBox.Password = entry.Password;
        NotesBox.Text = entry.Notes;
        StatusText.Text = "";
        EditorPanel.Visibility = Visibility.Visible;
        _loadingEditor = false;
    }

    private void ClearEditor()
    {
        _loadingEditor = true;
        _current = null;
        TitleBox.Text = UrlBox.Text = UserBox.Text = NotesBox.Text = "";
        PassBox.Password = "";
        StatusText.Text = "";
        EditorPanel.Visibility = Visibility.Collapsed;
        _loadingEditor = false;
    }

    private void OnNewClick(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
        var entry = new VaultEntry { Title = "New entry" };
        _session.Data!.Entries.Add(entry);
        RefreshList(entry.Id);
        LoadEditor(entry);
        TitleBox.Focus(FocusState.Programmatic);
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (_current is null || _loadingEditor) return;
        _current.Title = TitleBox.Text.Trim();
        _current.Url = UrlBox.Text.Trim();
        _current.Username = UserBox.Text;
        _current.Password = PassBox.Password;
        _current.Notes = NotesBox.Text;
        _current.ModifiedUtc = DateTimeOffset.UtcNow;

        await SaveVaultAsync("Saved.");
        RefreshList(_current.Id);
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var dialog = new ContentDialog
        {
            Title = "Delete entry?",
            Content = $"\"{_current.Title}\" will be permanently removed.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        _session.Data!.Entries.Remove(_current);
        ClearEditor();
        RefreshList();
        await SaveVaultAsync("Deleted.");
    }

    private async Task SaveVaultAsync(string okMessage)
    {
        try
        {
            await _session.SaveAsync();
            StatusText.Text = okMessage;
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not save: " + ex.Message;
        }
    }

    private void OnGenerateClick(object sender, RoutedEventArgs e) =>
        PassBox.Password = PasswordGenerator.Generate();

    private void OnCopyUserClick(object sender, RoutedEventArgs e)
    {
        if (UserBox.Text.Length == 0) return;
        ClipboardService.CopySensitive(UserBox.Text);
        StatusText.Text = $"Username copied (clears in {ClipboardService.ClearAfter.TotalSeconds:0}s).";
    }

    private void OnCopyPassClick(object sender, RoutedEventArgs e)
    {
        if (PassBox.Password.Length == 0) return;
        ClipboardService.CopySensitive(PassBox.Password);
        StatusText.Text = $"Password copied (clears in {ClipboardService.ClearAfter.TotalSeconds:0}s).";
    }

    // ---- Master password, import, export -------------------------------------------------

    private async void OnChangeMasterClick(object sender, RoutedEventArgs e)
    {
        var current = new PasswordBox { Header = "Current master password" };
        var next = new PasswordBox { Header = $"New master password (at least {MinMasterLength} characters)" };
        var confirm = new PasswordBox { Header = "Confirm new master password" };
        var error = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red), TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            Title = "Change master password",
            Content = new StackPanel { Spacing = 8, Children = { current, next, confirm, error } },
            PrimaryButtonText = "Change",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                if (next.Password.Length < MinMasterLength)
                    throw new InvalidOperationException($"Use at least {MinMasterLength} characters.");
                if (next.Password != confirm.Password)
                    throw new InvalidOperationException("The new passwords don't match.");
                await _session.ChangeMasterPasswordAsync(current.Password, next.Password);
                StatusText.Text = "Master password changed.";
            }
            catch (VaultAuthenticationException) { args.Cancel = true; error.Text = "Current master password is wrong."; }
            catch (InvalidOperationException ex) { args.Cancel = true; error.Text = ex.Message; }
            finally { deferral.Complete(); }
        };
        await dialog.ShowAsync();
    }

    private async void OnImportClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".csv");
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;
        try
        {
            var imported = CsvTransfer.Import(await File.ReadAllTextAsync(file.Path));
            _session.Data!.Entries.AddRange(imported);
            await SaveVaultAsync($"Imported {imported.Count} entries. Delete the CSV file now: it holds your passwords in plain text.");
            RefreshList();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Import failed: " + ex.Message;
        }
    }

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        var warn = new ContentDialog
        {
            Title = "Export unencrypted CSV?",
            Content = "The exported file contains ALL your passwords in plain text. Anyone who can read it can use them. " +
                      "Store it somewhere safe and delete it as soon as you are done.",
            PrimaryButtonText = "Export",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await warn.ShowAsync() != ContentDialogResult.Primary) return;

        var picker = new FileSavePicker { SuggestedFileName = "passwordkeeper-export" };
        picker.FileTypeChoices.Add("CSV", new List<string> { ".csv" });
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            await File.WriteAllTextAsync(file.Path, CsvTransfer.Export(_session.Data!.Entries));
            StatusText.Text = "Exported. Remember to delete the file when finished.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Export failed: " + ex.Message;
        }
    }
}
