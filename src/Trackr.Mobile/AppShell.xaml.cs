using Trackr.Mobile.Core.ViewModels;
using Trackr.Mobile.Pages;

namespace Trackr.Mobile;

public partial class AppShell : Shell
{
    public AppShell(AppShellViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = viewModel;

        // Registered here rather than declared as ShellContent because anything inside the
        // TabBar becomes a tab, and the profile is reached from the avatar instead. This is
        // also the app's only pushed route - see INavigationService.GoToProfileAsync.
        Routing.RegisterRoute(Routes.Profile, typeof(ProfilePage));
    }
}
