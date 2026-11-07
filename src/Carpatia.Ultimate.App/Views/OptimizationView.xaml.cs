using Carpatia.Ultimate.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Carpatia.Ultimate.App.Views;

public sealed partial class OptimizationView : UserControl
{
    public AppViewModel ViewModel { get; }
    public OptimizationView(AppViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        CpuEditor.Content = new OperationEditor(viewModel.CpuOperations);
        NetworkEditor.Content = new OperationEditor(viewModel.NetworkOperations);
        GpuFeatures.ItemsSource = FeatureCatalog.Gpu;
    }
}
