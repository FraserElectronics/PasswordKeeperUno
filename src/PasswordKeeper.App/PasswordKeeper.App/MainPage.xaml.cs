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
    private bool _dirty;

    public MainPage()
    {
        this.InitializeComponent();

        _idleTimer.Tick += (_, _) => LockVault();
        // Any interaction postpones the auto-lock.
        AddHandler(PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => ResetIdle()), true);
        AddHandler(KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, _) => ResetIdle()), true);

        PasswordBoxTab.AttachChain(MasterBox, ConfirmBox, ShowMasterCheck, UnlockButton);
        PasswordBoxTab.AttachChain(SearchBox, EntryList, TitleBox, UrlBox, UserBox, PassBox, NotesBox, SaveButton);

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
        UnlockButtonText.Text = creating ? "Create vault" : "Unlock";
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

    private async void LockVault()
    {
        _idleTimer.Stop();
        // Unsaved edits are saved rather than lost; never prompt here, since this also runs on idle.
        try { await FlushEditAsync(interactive: false); } catch { /* lock regardless */ }
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

    private async void OnEntrySelected(object sender, SelectionChangedEventArgs e)
    {
        if (EntryList.SelectedItem is not VaultEntry entry || ReferenceEquals(entry, _current)) return;
        if (_dirty && _current is not null)
        {
            await FlushEditAsync();
            RefreshList(entry.Id);
        }
        LoadEditor(entry);
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
        _dirty = false;
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
        _dirty = false;
    }

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        await FlushEditAsync();
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
        ApplyEditor();
        await SaveVaultAsync("Saved.");
        RefreshList(_current.Id);
    }

    private void ApplyEditor()
    {
        if (_current is null) return;
        _current.Title = TitleBox.Text.Trim();
        _current.Url = UrlBox.Text.Trim();
        _current.Username = UserBox.Text;
        _current.Password = PassBox.Password;
        _current.Notes = NotesBox.Text;
        _current.ModifiedUtc = DateTimeOffset.UtcNow;
        _dirty = false;
    }

    private void OnEditorChanged(object sender, TextChangedEventArgs e) => UpdateDirty();
    private void OnEditorPasswordChanged(object sender, RoutedEventArgs e) => UpdateDirty();

    // TextChanged can fire after a programmatic load, so compare with the entry instead of trusting it.
    private void UpdateDirty()
    {
        if (_loadingEditor || _current is null) return;
        _dirty = TitleBox.Text.Trim() != _current.Title ||
                 UrlBox.Text.Trim() != _current.Url ||
                 UserBox.Text != _current.Username ||
                 PassBox.Password != _current.Password ||
                 NotesBox.Text != _current.Notes;
        if (_dirty) StatusText.Text = "Unsaved changes (saved when you switch entry or lock).";
    }

    private async Task FlushEditAsync(bool interactive = true)
    {
        if (!_dirty || _current is null) return;
        ApplyEditor();
        await SaveVaultAsync("Saved.", interactive);
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

    private async Task<bool> SaveVaultAsync(string okMessage, bool interactive = true)
    {
        try
        {
            await _session.SaveAsync();
            StatusText.Text = okMessage;
            return true;
        }
        catch (VaultChangedOnDiskException)
        {
            if (!interactive) return false;
            return await ResolveChangedOnDiskAsync(okMessage);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not save: " + ex.Message;
            return false;
        }
    }

    private async Task<bool> ResolveChangedOnDiskAsync(string okMessage)
    {
        var dialog = new ContentDialog
        {
            Title = "Vault file changed",
            Content = "The vault file was changed outside this app since you opened it (another computer or a sync tool). " +
                      "Overwrite it with what you have here, or reload the version on disk? Reloading discards unsaved changes made here.",
            PrimaryButtonText = "Overwrite",
            SecondaryButtonText = "Reload",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        try
        {
            if (result == ContentDialogResult.Primary)
            {
                await _session.SaveAsync(overwrite: true);
                StatusText.Text = okMessage;
                return true;
            }
            if (result == ContentDialogResult.Secondary)
            {
                await _session.ReloadAsync();
                ClearEditor();
                RefreshList();
                StatusText.Text = "Reloaded from disk.";
            }
        }
        catch (VaultAuthenticationException)
        {
            // The file on disk now uses a different master password.
            LockVault();
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not complete that: " + ex.Message;
        }
        return false;
    }

    private async void OnOpenUrlClick(object sender, RoutedEventArgs e)
    {
        var text = UrlBox.Text.Trim();
        if (text.Length == 0) return;
        if (!text.Contains("://")) text = "https://" + text;
        // Only web links: a vault entry must never be able to launch other schemes (file:, custom handlers).
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            StatusText.Text = "Only http and https addresses can be opened.";
            return;
        }
        if (!await Launcher.LaunchUriAsync(uri)) StatusText.Text = "Could not open the browser.";
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
                var show = new CheckBox { Content = "Show passwords" };
        var counts = new TextBlock { Opacity = 0.7 };
        void Refresh(object? s, RoutedEventArgs? a)
        {
            var mode = show.IsChecked == true ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;
            current.PasswordRevealMode = next.PasswordRevealMode = confirm.PasswordRevealMode = mode;
            counts.Text = $"New: {next.Password.Length} characters, confirm: {confirm.Password.Length}. " +
                          (confirm.Password.Length == 0 ? "" : next.Password == confirm.Password ? "Match." : "Not matching yet.");
        }
        PasswordBoxTab.AttachChain(current, next, confirm, show);
        show.Checked += Refresh;
        show.Unchecked += Refresh;
        next.PasswordChanged += Refresh;
        confirm.PasswordChanged += Refresh;
        var error = new TextBlock { Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red), TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            Title = "Change master password",
            Content = new StackPanel { Spacing = 8, Children = { current, next, confirm, show, counts, error } },
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
            catch (VaultChangedOnDiskException) { args.Cancel = true; error.Text = "The vault file changed outside this app. Cancel, then save an entry to choose Overwrite or Reload first."; }
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
            // Skip rows identical to an entry already in the vault so re-importing doesn't duplicate.
            static string Key(VaultEntry v) => string.Join('\u001f', v.Title, v.Url, v.Username, v.Password, v.Notes);
            var existing = _session.Data!.Entries.Select(Key).ToHashSet();
            var fresh = imported.Where(v => existing.Add(Key(v))).ToList();
            _session.Data.Entries.AddRange(fresh);
            await SaveVaultAsync($"Imported {fresh.Count} entries, skipped {imported.Count - fresh.Count} duplicates. " +
                                 "Delete the CSV file now: it holds your passwords in plain text.");
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
