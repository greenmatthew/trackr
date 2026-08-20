using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class BodyMetricsPage : ContentPage
{
    private readonly BodyMetricsViewModel viewModel;

    public BodyMetricsPage(BodyMetricsViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = this.viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        _ = viewModel.LoadAsync();
    }
}
