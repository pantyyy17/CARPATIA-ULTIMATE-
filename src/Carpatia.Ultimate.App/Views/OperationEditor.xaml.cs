using Carpatia.Ultimate.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Carpatia.Ultimate.App.Views;

public sealed partial class OperationEditor : UserControl
{
    public OperationsViewModel ViewModel { get; }
    public OperationEditor(OperationsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += Editor_Loaded;
    }
    public Visibility BusyVisibility(bool busy) => busy ? Visibility.Visible : Visibility.Collapsed;
    private async void Editor_Loaded(object sender, RoutedEventArgs args)
    {
        Loaded -= Editor_Loaded;
        await ViewModel.LoadTargetsAsync();
    }
    private async void Refresh_Click(object sender, RoutedEventArgs args) => await ViewModel.LoadTargetsAsync();
    private async void Review_Click(object sender, RoutedEventArgs args)
    {
        var response = await ViewModel.ReadPreviewAsync();
        if (!IsLoaded || XamlRoot is null || response?.Preview is not { } preview) return;
        if (await ChangeReviewDialog.ShowAsync(XamlRoot, ActualTheme, preview, ViewModel.CanApply, "Review configuration change"))
            await ViewModel.ApplyReviewedAsync();
    }
}
