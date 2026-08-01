using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class HomePage : ContentPage
{
    public HomePage(HomeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
