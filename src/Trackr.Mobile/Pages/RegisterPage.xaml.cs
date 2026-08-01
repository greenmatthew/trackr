using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class RegisterPage : ContentPage
{
    private readonly RegisterViewModel _viewModel;

    public RegisterPage(RegisterViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    // Which registration path is open is a property of the server, not of this install, so it
    // is re-read on every visit rather than once at construction. Shell keeps a page instance
    // alive between navigations, and a server that gained its first account in the meantime
    // would otherwise still be offering to claim it.
    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);
    }
}
