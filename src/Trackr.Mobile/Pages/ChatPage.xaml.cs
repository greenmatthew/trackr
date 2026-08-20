using System.Collections.Specialized;
using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Pages;

public partial class ChatPage : ContentPage
{
    private readonly ChatViewModel viewModel;

    public ChatPage(ChatViewModel viewModel)
    {
        InitializeComponent();

        BindingContext = this.viewModel = viewModel;
    }

    /// <remarks>
    /// Subscribed here rather than in the constructor, and that is a consequence of the view model
    /// becoming a singleton: this page is still transient, Shell builds a fresh one from
    /// <c>ContentTemplate</c> each time the tab is visited, and a constructor subscription would
    /// leave every page this tab has ever had listening to the one surviving collection.
    /// <para>
    /// Detached first so a second <c>OnAppearing</c> without an intervening <c>OnDisappearing</c> -
    /// which Android does produce around backgrounding - subscribes once rather than twice.
    /// </para>
    /// </remarks>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        viewModel.Messages.CollectionChanged -= OnMessagesChanged;
        viewModel.Messages.CollectionChanged += OnMessagesChanged;

        // The transcript now outlives a visit to another tab, so coming back should land where the
        // conversation is rather than at the top of it.
        ScrollToNewest();
    }

    protected override void OnDisappearing()
    {
        viewModel.Messages.CollectionChanged -= OnMessagesChanged;

        base.OnDisappearing();
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not NotifyCollectionChangedAction.Add)
        {
            return;
        }

        ScrollToNewest();
    }

    /// <summary>
    /// Keeping the newest message in view.
    /// </summary>
    /// <remarks>
    /// The one piece of chat behaviour that cannot live in the view model: it is about where a list
    /// is scrolled, which Core has no idea about. Done here rather than in XAML because
    /// <c>CollectionView</c> has no "follow the tail" property.
    /// <para>
    /// Dispatched because a freshly added item has not been measured at the moment it arrives, and
    /// scrolling to something with no height yet lands short of the bottom.
    /// </para>
    /// </remarks>
    private void ScrollToNewest()
    {
        if (viewModel.Messages.Count == 0)
        {
            return;
        }

        Dispatcher.Dispatch(() =>
            Transcript.ScrollTo(viewModel.Messages.Count - 1, position: ScrollToPosition.End));
    }
}
