using Trackr.Mobile.Core.ViewModels;
using Trackr.Mobile.Pages;

namespace Trackr.Mobile;

public partial class AppShell : Shell
{
    private readonly AppShellViewModel _viewModel;

    public AppShell(AppShellViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        BindingContext = viewModel;

        // Registered here rather than declared as ShellContent because anything inside the
        // TabBar becomes a tab, and the profile is reached from the avatar instead. This is
        // also the app's only pushed route - see INavigationService.GoToProfileAsync.
        Routing.RegisterRoute(Routes.Profile, typeof(ProfilePage));
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Fetching the avatar is a network call and this shell has just replaced the loading
        // screen, so it is started rather than awaited: the tabs are usable immediately and
        // the initials circle becomes a photograph a moment later. A server that cannot be
        // reached is already handled below the command and leaves the initials showing;
        // anything else is a bug, and the command rethrowing it is how it gets noticed.
        _viewModel.LoadAvatarCommand.Execute(null);
    }
}
