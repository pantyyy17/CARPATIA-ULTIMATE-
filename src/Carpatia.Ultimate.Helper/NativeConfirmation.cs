using System.Runtime.InteropServices;
using Carpatia.Ultimate.Broker;

namespace Carpatia.Ultimate.Helper;

public static class NativeConfirmation
{
    public static Task<bool> ConfirmAsync(ControlPreview preview)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult(false);
        var text = preview.Description + "\n\nResource: " + preview.ResourceId
            + "\n\nCurrent state:\n" + preview.Before.GetRawText()
            + "\n\nRequested state:\n" + preview.Expected.GetRawText()
            + "\n\nApply this exact change? The original configuration will be recorded before writing.";
        // Never show a shortened confirmation that conceals part of a change.
        if (text.Length > 12000) return Task.FromResult(false);
        const uint yesNo = 0x00000004, warning = 0x00000030, defaultNo = 0x00000100,
            foreground = 0x00010000, topMost = 0x00040000;
        return Task.FromResult(MessageBoxW(IntPtr.Zero, text,
            "CARPATIA ULTIMATE EDITION — Confirm system change",
            yesNo | warning | defaultNo | foreground | topMost) == 6);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
