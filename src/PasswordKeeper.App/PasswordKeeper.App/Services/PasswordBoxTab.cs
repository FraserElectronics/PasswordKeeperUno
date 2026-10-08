using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace PasswordKeeper.App.Services;

/// <summary>
/// Uno's PasswordBox on macOS types Tab into the password as a character. Make Tab move
/// focus to the next field instead, like a normal Windows/macOS form field.
/// </summary>
public static class PasswordBoxTab
{
    public static void Attach(PasswordBox box) => box.KeyDown += OnKeyDown;

    private static void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Tab) return;
        e.Handled = true;
        FocusManager.TryMoveFocus(FocusNavigationDirection.Next);
    }
}
