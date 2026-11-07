using Carpatia.Ultimate.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Carpatia.Ultimate.App.Views;

public sealed partial class SettingsView : UserControl
{
    private readonly Action<ElementTheme> _setTheme;
    private readonly AppViewModel _viewModel;
    public SettingsView(AppViewModel viewModel, Action<ElementTheme> setTheme)
    {
        _viewModel = viewModel;
        _setTheme = setTheme;
        InitializeComponent();
    }
    private void Theme_SelectionChanged(object sender, SelectionChangedEventArgs args)
    {
        if (sender is ComboBox box)
            _setTheme(box.SelectedIndex switch { 1 => ElementTheme.Dark, 2 => ElementTheme.Default, _ => ElementTheme.Light });
    }
    private void Copy_Click(object sender, RoutedEventArgs args)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(_viewModel.CreateDiagnosticReport());
            Clipboard.SetContent(package);
            CopyStatus.Text = "Diagnostic summary copied. You choose where to share it.";
        }
        catch (Exception error) when (error is not OutOfMemoryException) { CopyStatus.Text = $"Could not access the clipboard: {error.Message}"; }
    }
}
