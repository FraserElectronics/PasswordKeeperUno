using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace PasswordKeeper.App.Services;

/// <summary>
/// Uno's text fields on macOS type Tab as a character and don't reliably move focus. Intercept Tab and
/// focus the next visible, enabled control in an explicit order instead (wrapping round at the end).
/// </summary>
public static class PasswordBoxTab
{
    public static void AttachChain(params Control[] order)
    {
        for (var i = 0; i < order.Length; i++)
        {
            var index = i;
            order[i].KeyDown += (_, e) =>
            {
                if (e.Key != VirtualKey.Tab) return;
                e.Handled = true;
                for (var step = 1; step < order.Length; step++)
                {
                    var next = order[(index + step) % order.Length];
                    if (next.Visibility == Visibility.Visible && next.IsEnabled && next.Focus(FocusState.Keyboard)) return;
                }
            };
        }
    }
}
