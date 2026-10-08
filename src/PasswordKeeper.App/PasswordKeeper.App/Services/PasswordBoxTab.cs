using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace PasswordKeeper.App.Services;

/// <summary>
/// Uno's PasswordBox on macOS types Tab into the password as a character. Make Tab / Shift+Tab move
/// focus instead, like a normal Windows/macOS form field.
/// </summary>
public static class PasswordBoxTab
{
    public static void Attach(PasswordBox box) => box.KeyDown += OnKeyDown;

    private static void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Tab) return;
        e.Handled = true;
        var shift = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                     & Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
        FocusManager.TryMoveFocus(shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next);
    }
}
