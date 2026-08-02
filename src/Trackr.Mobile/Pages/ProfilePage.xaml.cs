using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class ProfilePage : ContentPage
{
    private readonly ProfileViewModel _viewModel;

    public ProfilePage(ProfileViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // The shell asks for the picture too, but this page can also be reached after it
        // changed on another device. A repeat call is cheap: the store only reaches the
        // network when the account's marker says the server's copy has moved.
        _viewModel.LoadCommand.Execute(null);
    }
}
