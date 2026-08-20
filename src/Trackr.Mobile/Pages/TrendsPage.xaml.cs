using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class TrendsPage : ContentPage
{
    private readonly TrendsViewModel viewModel;

    public TrendsPage(TrendsViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = this.viewModel = viewModel;
    }

    /// <remarks>
    /// Refetched on every appearance rather than once. Meals are confirmed on another tab, so a
    /// summary loaded once would be stale by exactly the moment somebody comes to look at it.
    /// </remarks>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        _ = viewModel.RefreshAsync();
    }
}
