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

        // Keeping the newest message in view is the one piece of chat behaviour that cannot live in
        // the view model: it is about where a list is scrolled, which Core has no idea about. Bound
        // here rather than in XAML because CollectionView has no "follow the tail" property.
        viewModel.Messages.CollectionChanged += OnMessagesChanged;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not NotifyCollectionChangedAction.Add || viewModel.Messages.Count == 0)
        {
            return;
        }

        // Dispatched because the item has not been measured at the moment it is added, and scrolling
        // to something with no height yet lands short of the bottom.
        Dispatcher.Dispatch(() =>
            Transcript.ScrollTo(viewModel.Messages.Count - 1, position: ScrollToPosition.End));
    }
}
