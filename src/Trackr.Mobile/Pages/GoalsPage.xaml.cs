using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class GoalsPage : ContentPage
{
    private readonly GoalsViewModel viewModel;

    public GoalsPage(GoalsViewModel viewModel)
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
