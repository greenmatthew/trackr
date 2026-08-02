namespace Trackr.Mobile;

public partial class AuthShell : Shell
{
    /// <param name="hasServer">
    /// Whether a server address is already stored. False on a genuine first run, and after
    /// "change server" on the login screen.
    /// </param>
    public AuthShell(bool hasServer)
    {
        InitializeComponent();

        // Selecting the starting screen here rather than navigating to it after the shell
        // appears. The old startup path opened on whatever route came first and corrected
        // itself a moment later, which is the flicker milestone 5 exists to remove.
        if (hasServer)
        {
            CurrentItem = LoginContent;
        }
    }
}
