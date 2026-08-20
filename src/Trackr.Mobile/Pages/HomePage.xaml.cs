using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class HomePage : ContentPage
{
    private readonly HomeViewModel viewModel;

    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = this.viewModel = viewModel;
    }

    /// <remarks>
    /// Refetched on every appearance. Logging a meal and coming straight back to watch the day move
    /// is the loop this tab exists for, and it happens on the tab next door.
    /// </remarks>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        _ = viewModel.RefreshAsync();
    }
}
