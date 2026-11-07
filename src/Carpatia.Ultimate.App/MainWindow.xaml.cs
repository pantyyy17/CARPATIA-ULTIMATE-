using Carpatia.Ultimate.App.ViewModels;
using Carpatia.Ultimate.App.Views;
using Carpatia.Ultimate.Diagnostics;
using Carpatia.Ultimate.Engine;
using Carpatia.Ultimate.Broker;
using Carpatia.Ultimate.Control;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace Carpatia.Ultimate.App;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, UserControl> _views = new();
    public AppViewModel ViewModel { get; }

    public MainWindow()
    {
        var journal = new FileOperationJournal(RecoveryStoreLocation.DirectoryPath);
        var control = new WindowsControlRuntime(journal);
        ViewModel = new AppViewModel(new WindowsHardwareDiagnostics(), journal, control);
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleDragArea);
        AppWindow.Resize(new SizeInt32(1380, 920));
        if (AppWindow.TitleBar is { } bar)
        {
            bar.ButtonBackgroundColor = Colors.Transparent;
            bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
        Root.Loaded += Root_Loaded;
        Navigation.SelectedItem = Navigation.MenuItems[0];
    }

    private async void Root_Loaded(object sender, RoutedEventArgs args)
    {
        Root.Loaded -= Root_Loaded;
        await ViewModel.LoadRecoveryAsync();
        await ViewModel.RefreshAsync();
    }

    private void Navigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string key }) ShowPage(key);
    }

    public void NavigateTo(string key)
    {
        var item = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
            .OfType<NavigationViewItem>().FirstOrDefault(x => x.Tag as string == key);
        if (item is not null) Navigation.SelectedItem = item;
    }

    private void ShowPage(string key)
    {
        if (!_views.TryGetValue(key, out var view))
        {
            view = key switch
            {
                "Dashboard" => new DashboardView(ViewModel, () => NavigateTo("Optimization"), () => NavigateTo("Backup & Restore")),
                "Optimization" => new OptimizationView(ViewModel),
                "System Tweaks" => new CatalogView("System Tweaks", "Thoughtful configuration. Every change accountable.", FeatureCatalog.System),
                "Debloat" => new CatalogView("Debloat", "Keep what matters. Remove only what you choose.", FeatureCatalog.Debloat),
                "Windows Services" => new CatalogView("Windows Services", "Understand dependencies before changing a service.", FeatureCatalog.Services),
                "Backup & Restore" => new RecoveryView(ViewModel),
                "Settings" => new SettingsView(ViewModel, theme => Root.RequestedTheme = theme),
                _ => throw new ArgumentOutOfRangeException(nameof(key))
            };
            _views.Add(key, view);
        }
        PageContent.Content = view;
    }
}
