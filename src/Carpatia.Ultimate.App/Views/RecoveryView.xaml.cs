using Carpatia.Ultimate.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage;
using Windows.System;

namespace Carpatia.Ultimate.App.Views;

public sealed partial class RecoveryView : UserControl
{
    public AppViewModel ViewModel { get; }
    public RecoveryView(AppViewModel viewModel) { ViewModel = viewModel; InitializeComponent(); }
    private async void Refresh_Click(object sender, RoutedEventArgs args) => await ViewModel.LoadRecoveryAsync();
    private async void Restore_Click(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { Tag: Guid id } button) return;
        button.IsEnabled = false;
        try
        {
            var response = await ViewModel.ReadRestorePreviewAsync(id);
            if (!IsLoaded || XamlRoot is null) return;
            RestoreStatus.Text = $"{response.Status}: {response.Message}";
            if (response.Preview is { } preview && await ChangeReviewDialog.ShowAsync(XamlRoot, ActualTheme, preview, true, "Review original configuration"))
            {
                var result = await ViewModel.RestoreReviewedAsync(id, preview);
                RestoreStatus.Text = $"{result.Status}: {result.Message}";
            }
        }
        finally { button.IsEnabled = true; }
    }
    private async void OpenFolder_Click(object sender, RoutedEventArgs args)
    {
        string? message = null;
        try
        {
            if (!Directory.Exists(ViewModel.JournalDirectory))
                message = "The protected journal folder is created by the one-shot elevated helper. There are no recorded operations in this location yet.";
            else
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(ViewModel.JournalDirectory);
                if (!await Launcher.LaunchFolderAsync(folder)) message = "Windows could not open the journal folder.";
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException) { message = error.Message; }
        if (message is not null)
            await ChangeReviewDialog.ShowMessageAsync(XamlRoot, ActualTheme, "Recovery folder", message);
    }
}
