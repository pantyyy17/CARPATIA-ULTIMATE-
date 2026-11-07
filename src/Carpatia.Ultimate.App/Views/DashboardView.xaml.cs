using Carpatia.Ultimate.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Carpatia.Ultimate.App.Views;

public sealed partial class DashboardView : UserControl
{
    private readonly Action _review;
    private readonly Action _recovery;
    public AppViewModel ViewModel { get; }
    public DashboardView(AppViewModel viewModel, Action review, Action recovery)
    {
        ViewModel = viewModel;
        _review = review;
        _recovery = recovery;
        InitializeComponent();
    }
    private void Review_Click(object sender, RoutedEventArgs args) => _review();
    private void Recovery_Click(object sender, RoutedEventArgs args) => _recovery();
    private async void Refresh_Click(object sender, RoutedEventArgs args) => await ViewModel.RefreshAsync();
    private void Profile_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is ComboBox { SelectedItem: string profile }) ViewModel.SelectedProfile = profile;
    }
}
