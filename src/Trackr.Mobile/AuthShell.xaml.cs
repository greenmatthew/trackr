using Trackr.Mobile.Core.Auth;

namespace Trackr.Mobile;

public partial class AuthShell : Shell
{
    public AuthShell(AuthSession session)
    {
        InitializeComponent();

        // Selecting the starting screen here rather than navigating to it after the shell
        // appears. The old startup path opened on whatever route came first and corrected
        // itself a moment later, which is the flicker milestone 5 exists to remove.
        //
        // HasServer is false on a genuine first run, and after "use a different server" on
        // the login screen.
        if (session.HasServer)
        {
            CurrentItem = LoginContent;
        }
    }
}
