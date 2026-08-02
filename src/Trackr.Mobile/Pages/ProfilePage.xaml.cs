using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class ProfilePage : ContentPage
{
    public ProfilePage(ProfileViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;
    }
}
