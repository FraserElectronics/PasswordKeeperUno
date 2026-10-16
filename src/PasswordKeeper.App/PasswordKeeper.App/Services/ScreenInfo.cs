using System;
using System.Runtime.InteropServices;

namespace PasswordKeeper.App.Services;

/// <summary>Primary display size on desktop (macOS and Windows), used to centre the window at start-up.</summary>
internal static class ScreenInfo
{
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern uint CGMainDisplayID();

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern nuint CGDisplayPixelsWide(uint display);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern nuint CGDisplayPixelsHigh(uint display);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static bool TryGetPrimarySize(out int width, out int height)
    {
        width = height = 0;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                var id = CGMainDisplayID();
                width = (int)CGDisplayPixelsWide(id);
                height = (int)CGDisplayPixelsHigh(id);
            }
            else if (OperatingSystem.IsWindows())
            {
                width = GetSystemMetrics(0);   // SM_CXSCREEN
                height = GetSystemMetrics(1);  // SM_CYSCREEN
            }
        }
        catch
        {
            // Not fatal: the window just opens where the OS puts it.
        }
        return width > 0 && height > 0;
    }
}
