using System.Text.Json;
using System.Runtime.InteropServices;
using Carpatia.Ultimate.Broker;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Carpatia.Ultimate.App.Views;

internal static class ChangeReviewDialog
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<bool> ShowAsync(XamlRoot? root, ElementTheme theme, ControlPreview preview, bool canApply, string title)
    {
        if (root is null || !await Gate.WaitAsync(0)) return false;
        try
        {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(new TextBlock { Text = preview.Description, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(new TextBlock { Text = "Current configuration", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(Snapshot(JsonSerializer.Serialize(preview.Before, options)));
        content.Children.Add(new TextBlock { Text = "Requested configuration", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(Snapshot(JsonSerializer.Serialize(preview.Expected, options)));
        content.Children.Add(new TextBlock { Text = canApply
            ? "Windows will request administrator permission. The helper checks this plan again and asks for a final confirmation. No performance increase is guaranteed."
            : "The requested configuration is already present. No write or administrator request is needed.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        var dialog = new ContentDialog
        {
            XamlRoot = root, RequestedTheme = theme, Title = title,
            Content = new ScrollViewer { Content = content, MaxHeight = 480, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            CloseButtonText = "Close", DefaultButton = ContentDialogButton.Close
        };
        if (canApply) dialog.PrimaryButtonText = "Continue to administrator review";
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException) { return false; }
        finally { Gate.Release(); }
    }

    public static async Task ShowMessageAsync(XamlRoot? root, ElementTheme theme, string title, string message)
    {
        if (root is null || !await Gate.WaitAsync(0)) return;
        try
        {
            await new ContentDialog { XamlRoot = root, RequestedTheme = theme, Title = title,
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "Close" }.ShowAsync();
        }
        catch (Exception error) when (error is COMException or InvalidOperationException or ArgumentException) { }
        finally { Gate.Release(); }
    }

    private static TextBox Snapshot(string text) => new()
    {
        Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
        FontFamily = new FontFamily("Consolas"), FontSize = 12, MaxHeight = 180
    };
}
