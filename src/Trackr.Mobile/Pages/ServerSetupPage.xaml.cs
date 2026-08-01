using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class ServerSetupPage : ContentPage
{
    // The view model arrives by constructor injection: Shell resolves pages from the
    // container because MauiProgram registers them there.
    public ServerSetupPage(ServerSetupViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
