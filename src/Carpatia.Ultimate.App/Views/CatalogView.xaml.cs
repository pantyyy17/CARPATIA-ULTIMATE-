using Carpatia.Ultimate.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Carpatia.Ultimate.App.Views;

public sealed partial class CatalogView : UserControl
{
    public string Heading { get; }
    public string Description { get; }
    public IReadOnlyList<FeatureItem> Features { get; }
    public CatalogView(string heading, string description, IReadOnlyList<FeatureItem> features)
    {
        Heading = heading;
        Description = description;
        Features = features;
        InitializeComponent();
    }
}
