using PasswordKeeper.App.Services;
using PasswordKeeper.Core.Cloud;
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
    private bool _cloudBusy;

    public MainPage()
    {
        this.InitializeComponent();

        VaultPanel.SizeChanged += (_, _) => ApplyLayout();
        _idleTimer.Tick += (_, _) => LockVault();
        // Any interaction postpones the auto-lock.
        AddHandler(PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => ResetIdle()), true);
        AddHandler(KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, _) => ResetIdle()), true);

#if __IOS__ || __ANDROID__
        // Leave room beneath the card so it can scroll clear of the on-screen keyboard.
        UnlockCard.Margin = new Thickness(16, 16, 16, 340);
        // The app draws edge to edge, so keep the vault view clear of the status bar / camera cut-out
        // and the home indicator.
#if __IOS__
        VaultPanel.Padding = new Thickness(16, 64, 16, 28);
#else
        VaultPanel.Padding = new Thickness(16, 40, 16, 24);
#endif
#endif
        NotesBox.FontFamily = new FontFamily(OperatingSystem.IsWindows() ? "Consolas" : "Menlo");
        PasswordBoxTab.AttachChain(MasterBox, ConfirmBox, ShowMasterCheck, UnlockButton);
        PasswordBoxTab.AttachChain(SearchBox, EntryList, TitleBox, UrlBox, UserBox, PassBox, ShowPassCheck, NotesBox, SaveButton);

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

    private void OnShowPassChanged(object sender, RoutedEventArgs e) =>
        PassBox.PasswordRevealMode = ShowPassCheck.IsChecked == true ? PasswordRevealMode.Visible : PasswordRevealMode.Hidden;

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
#if __IOS__ || __ANDROID__
        picker.FileTypeFilter.Add("*"); // phones don't know the .pkv type, so let any file be chosen
#endif
        var file = await picker.PickSingleFileAsync();
        if (file is null) return;

#if __IOS__ || __ANDROID__
        // Phones can't keep using a file outside the app's sandbox, so work on a private copy.
        try
        {
            var dir = Path.GetDirectoryName(VaultSession.DefaultPath())!;
            var target = Path.Combine(dir, "imported-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".pkv");
            File.Copy(file.Path, target, overwrite: false);
            UseVaultPath(target);
        }
        catch (Exception ex)
        {
            UnlockError.Text = "Could not import that file: " + ex.Message;
        }
#else
        UseVaultPath(file.Path);
#endif
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
        CloudStatusText.Text = "";
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

    // ---- Layout (side by side on desktop, one pane at a time on a phone) ------------------

    private bool IsNarrow => VaultPanel.ActualWidth > 0 && VaultPanel.ActualWidth < 700;

    private void ApplyLayout()
    {
        var cols = VaultPanel.ColumnDefinitions;
        var star = new GridLength(1, GridUnitType.Star);
        BackButton.Visibility = IsNarrow && _current is not null ? Visibility.Visible : Visibility.Collapsed;

        if (!IsNarrow)
        {
            cols[0].Width = new GridLength(300);
            cols[1].Width = star;
            ListPane.Visibility = EditorScroll.Visibility = Visibility.Visible;
            return;
        }

        var showEditor = _current is not null;
        cols[0].Width = showEditor ? new GridLength(0) : star;
        cols[1].Width = showEditor ? star : new GridLength(0);
        ListPane.Visibility = showEditor ? Visibility.Collapsed : Visibility.Visible;
        EditorScroll.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnBackClick(object sender, RoutedEventArgs e)
    {
        await FlushEditAsync();
        EntryList.SelectedItem = null;
        ClearEditor();
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
        UpdateHealth();
        ApplyLayout();
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
        UpdateHealth();
        ApplyLayout();
    }

    private async void OnNewClick(object sender, RoutedEventArgs e)
    {
        await FlushEditAsync();
        SearchBox.Text = "";
        var entry = new VaultEntry { Title = "" };
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
        _current.SetPassword(PassBox.Password);
        _current.Notes = NotesBox.Text;
        _current.ModifiedUtc = DateTimeOffset.UtcNow;
        _dirty = false;
    }

    private void OnEditorChanged(object sender, TextChangedEventArgs e) => UpdateDirty();
    private void OnEditorPasswordChanged(object sender, RoutedEventArgs e)
    {
        UpdateDirty();
        UpdateHealth();
    }

    private void UpdateHealth()
    {
        var password = PassBox.Password;
        var level = PasswordStrength.Evaluate(password);
        var (label, color) = level switch
        {
            PasswordStrengthLevel.Weak => ("Strength: weak", Microsoft.UI.Colors.OrangeRed),
            PasswordStrengthLevel.Fair => ("Strength: fair", Microsoft.UI.Colors.Orange),
            PasswordStrengthLevel.Good => ("Strength: good", Microsoft.UI.Colors.YellowGreen),
            PasswordStrengthLevel.Strong => ("Strength: strong", Microsoft.UI.Colors.LimeGreen),
            _ => ("", Microsoft.UI.Colors.Gray),
        };
        StrengthText.Text = label;
        StrengthText.Foreground = new SolidColorBrush(color);

        var shared = _current is null || !_session.IsUnlocked
            ? new List<VaultEntry>()
            : VaultAudit.EntriesSharingPassword(password, _current.Id, _session.Data!.Entries);
        ReuseText.Text = shared.Count == 0
            ? ""
            : "This password is also used by: " + string.Join(", ", shared.Select(e => e.Title));
    }

    private async void OnHistoryClick(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        var items = _current.PasswordHistory;
        if (items.Count == 0)
        {
            StatusText.Text = "No previous passwords for this entry.";
            return;
        }
        var list = new ListView
        {
            ItemsSource = items.Select(i =>
                $"{i.ChangedUtc.ToLocalTime():yyyy-MM-dd HH:mm}    {new string('\u2022', Math.Min(i.Password.Length, 12))}").ToList(),
            SelectedIndex = 0,
            MaxHeight = 240,
        };
        var dialog = new ContentDialog
        {
            Title = "Previous passwords",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Each line shows when that password was replaced.", TextWrapping = TextWrapping.Wrap },
                    list,
                },
            },
            PrimaryButtonText = "Restore",
            SecondaryButtonText = "Copy",
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        var index = list.SelectedIndex;
        if (index < 0 || index >= items.Count) return;
        if (result == ContentDialogResult.Primary)
        {
            PassBox.Password = items[index].Password;
            StatusText.Text = "Restored. Save to keep it.";
        }
        else if (result == ContentDialogResult.Secondary)
        {
            ClipboardService.CopySensitive(items[index].Password);
            StatusText.Text = $"Old password copied (clears in {ClipboardService.ClearAfter.TotalSeconds:0}s).";
        }
    }

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
            AutoBackupAfterSave();
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
                AutoBackupAfterSave();
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

    // ---- Cloud backup (Backblaze B2) -------------------------------------------------------

    private void AutoBackupAfterSave()
    {
        if (_session.Data?.CloudBackup is { AutoBackup: true, IsComplete: true })
            _ = RunBackupAsync(interactive: false);
    }

    private async Task RunBackupAsync(bool interactive)
    {
        var settings = _session.Data?.CloudBackup;
        if (settings is null || !settings.IsComplete || _cloudBusy) return;
        _cloudBusy = true;
        var restoreNext = false;
        CloudStatusText.Text = "Backing up to Backblaze...";
        try
        {
            var outcome = await CloudBackupService.BackupAsync(_session, settings, overwriteCloud: false);
            if (outcome == BackupOutcome.CloudChanged)
            {
                if (!interactive)
                {
                    CloudStatusText.Text = "The cloud copy was changed on another device. Open Cloud backup to choose what to keep.";
                    return;
                }
                var dialog = new ContentDialog
                {
                    Title = "Cloud copy is different",
                    Content = "The copy in Backblaze was changed since this device last synced (probably from another device). " +
                              "Overwrite it with this vault, or replace this vault with the cloud copy?",
                    PrimaryButtonText = "Overwrite cloud copy",
                    SecondaryButtonText = "Restore cloud copy",
                    CloseButtonText = "Cancel",
                    XamlRoot = XamlRoot,
                };
                var result = await dialog.ShowAsync();
                if (result == ContentDialogResult.Primary)
                {
                    await CloudBackupService.BackupAsync(_session, settings, overwriteCloud: true);
                    CloudStatusText.Text = $"Backed up to Backblaze at {DateTime.Now:HH:mm}.";
                }
                else if (result == ContentDialogResult.Secondary)
                {
                    restoreNext = true;
                }
                else
                {
                    CloudStatusText.Text = "Backup cancelled.";
                }
                return;
            }
            CloudStatusText.Text = $"Backed up to Backblaze at {DateTime.Now:HH:mm}.";
        }
        catch (Exception ex)
        {
            CloudStatusText.Text = "Cloud backup failed: " + ex.Message;
        }
        finally
        {
            _cloudBusy = false;
            if (restoreNext) _ = RestoreCloudCopyAsync(settings);
        }
    }

    private async Task RestoreCloudCopyAsync(CloudBackupSettings settings)
    {
        if (_cloudBusy) return;
        var confirm = new ContentDialog
        {
            Title = "Replace this vault with the cloud copy?",
            Content = $"The vault on this device ({_session.Path}) will be replaced by the copy in Backblaze. " +
                      "Your current file is kept beside it with the ending .before-restore. " +
                      "You will need the master password of the cloud copy.",
            PrimaryButtonText = "Replace",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        _cloudBusy = true;
        CloudStatusText.Text = "Restoring from Backblaze...";
        try
        {
            await FlushEditAsync(interactive: false);
            await CloudBackupService.RestoreAsync(_session.Path, settings);
            _idleTimer.Stop();
            _session.Lock();
            _current = null;
            ClearEditor();
            EntryList.ItemsSource = null;
            SearchBox.Text = "";
            CloudStatusText.Text = "";
            ShowUnlock();
            UnlockHint.Text = "Restored from Backblaze. Enter the master password of the restored vault.";
            await ShowRestoreSuccessAsync(settings);
        }
        catch (Exception ex)
        {
            CloudStatusText.Text = "Restore failed: " + ex.Message;
        }
        finally
        {
            _cloudBusy = false;
        }
    }

    private Task<ContentDialogResult> ShowRestoreSuccessAsync(CloudBackupSettings settings) =>
        new ContentDialog
        {
            Title = "Restore complete",
            Content = $"Downloaded \"{settings.FileName}\" from Backblaze bucket \"{settings.Bucket}\" and saved it to {_session.Path}. " +
                      "Enter the master password to unlock it.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        }.ShowAsync().AsTask();

    private async void OnCloudBackupClick(object sender, RoutedEventArgs e)
    {
        await FlushEditAsync();
        var form = new CloudForm(_session.Data!.CloudBackup, withAutoBackup: true,
            "Your vault stays encrypted: only the encrypted file is uploaded, so Backblaze never sees your passwords. " +
            "Use a private bucket and an application key limited to it (see docs/CLOUD.md). " +
            "These settings are stored inside your encrypted vault.");
        var dialog = new ContentDialog
        {
            Title = "Cloud backup (Backblaze B2)",
            Content = new ScrollViewer { Content = form.Panel, MaxHeight = 460 },
            PrimaryButtonText = "Back up now",
            SecondaryButtonText = "Restore from cloud",
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        };
        void Validate(ContentDialogButtonClickEventArgs args)
        {
            if (form.Read().IsComplete) return;
            args.Cancel = true;
            form.Error.Text = "Fill in the key ID, application key, bucket and file name.";
        }
        dialog.PrimaryButtonClick += (_, args) => Validate(args);
        dialog.SecondaryButtonClick += (_, args) => Validate(args);

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;

        var settings = form.Read();
        if (result == ContentDialogResult.Secondary)
        {
            await RestoreCloudCopyAsync(settings);
            return;
        }

        // Keep the settings inside the vault, save it, then upload the freshly saved file.
        _session.Data!.CloudBackup = settings;
        _cloudBusy = true; // the save below must not start its own automatic backup
        bool saved;
        try { saved = await SaveVaultAsync("Saved."); }
        finally { _cloudBusy = false; }
        if (saved) await RunBackupAsync(interactive: true);
    }

    private async void OnRestoreFromCloudClick(object sender, RoutedEventArgs e)
    {
        var form = new CloudForm(null, withAutoBackup: false,
            "Enter your Backblaze details to download your vault to this device. " +
            $"If a vault already exists at {_session.Path} it is kept beside it with the ending .before-restore.");
        var dialog = new ContentDialog
        {
            Title = "Restore from cloud",
            Content = new ScrollViewer { Content = form.Panel, MaxHeight = 460 },
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            XamlRoot = XamlRoot,
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (form.Read().IsComplete) return;
            args.Cancel = true;
            form.Error.Text = "Fill in the key ID, application key, bucket and file name.";
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        UnlockError.Text = "";
        try
        {
            await CloudBackupService.RestoreAsync(_session.Path, form.Read());
            ShowUnlock();
            UnlockHint.Text = "Restored from Backblaze. Enter your master password.";
            await ShowRestoreSuccessAsync(form.Read());
        }
        catch (Exception ex)
        {
            UnlockError.Text = "Restore failed: " + ex.Message;
        }
    }

    /// <summary>The Backblaze connection fields shared by the backup and restore dialogs.</summary>
    private sealed class CloudForm
    {
        private readonly TextBox _keyId = new() { Header = "Application key ID" };
        private readonly PasswordBox _appKey = new() { Header = "Application key" };
        private readonly TextBox _bucket = new() { Header = "Bucket name" };
        private readonly TextBox _fileName = new() { Header = "File name in the bucket", Text = "vault.pkv" };
        private readonly CheckBox _showKey = new() { Content = "Show application key" };
        private readonly CheckBox _auto = new() { Content = "Back up automatically after every save" };
        private readonly bool _withAuto;

        public TextBlock Error { get; } = new() { Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red), TextWrapping = TextWrapping.Wrap };
        public StackPanel Panel { get; }

        public CloudForm(CloudBackupSettings? initial, bool withAutoBackup, string intro)
        {
            _withAuto = withAutoBackup;
            if (initial is not null)
            {
                _keyId.Text = initial.KeyId;
                _appKey.Password = initial.ApplicationKey;
                _bucket.Text = initial.Bucket;
                _fileName.Text = initial.FileName;
                _auto.IsChecked = initial.AutoBackup;
            }
            _showKey.Checked += (_, _) => _appKey.PasswordRevealMode = PasswordRevealMode.Visible;
            _showKey.Unchecked += (_, _) => _appKey.PasswordRevealMode = PasswordRevealMode.Hidden;

            Panel = new StackPanel { Spacing = 8 };
            Panel.Children.Add(new TextBlock { Text = intro, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
            Panel.Children.Add(WithPaste(_keyId, t => _keyId.Text = t.Trim()));
            Panel.Children.Add(WithPaste(_appKey, t => _appKey.Password = t.Trim()));
            Panel.Children.Add(_showKey);
            Panel.Children.Add(WithPaste(_bucket, t => _bucket.Text = t.Trim()));
            Panel.Children.Add(_fileName);
            if (withAutoBackup) Panel.Children.Add(_auto);
            Panel.Children.Add(Error);

            if (withAutoBackup) PasswordBoxTab.AttachChain(_keyId, _appKey, _showKey, _bucket, _fileName, _auto);
            else PasswordBoxTab.AttachChain(_keyId, _appKey, _showKey, _bucket, _fileName);
        }

        // Cmd/Ctrl+V does not always reach text boxes inside Uno dialogs, so offer an explicit Paste button.
        private static Grid WithPaste(Control field, Action<string> apply)
        {
            var button = new Button { Content = "Paste", VerticalAlignment = VerticalAlignment.Bottom, IsTabStop = false };
            button.Click += async (_, _) =>
            {
                try
                {
                    var text = await Windows.ApplicationModel.DataTransfer.Clipboard.GetContent().GetTextAsync();
                    if (!string.IsNullOrEmpty(text)) apply(text);
                }
                catch { /* clipboard unavailable */ }
            };
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(field);
            Grid.SetColumn(button, 1);
            grid.Children.Add(button);
            return grid;
        }

        public CloudBackupSettings Read() => new()
        {
            KeyId = _keyId.Text.Trim(),
            ApplicationKey = _appKey.Password.Trim(),
            Bucket = _bucket.Text.Trim(),
            FileName = _fileName.Text.Trim(),
            AutoBackup = _withAuto && _auto.IsChecked == true,
        };
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
                AutoBackupAfterSave();
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
