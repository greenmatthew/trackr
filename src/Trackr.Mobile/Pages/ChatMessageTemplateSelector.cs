using Trackr.Mobile.Core.ViewModels.Chat;

namespace Trackr.Mobile.Pages;

/// <summary>
/// Picks the template for one line of the conversation.
/// </summary>
/// <remarks>
/// The four kinds are drawn completely differently - a bubble, a sentence, a warning strip and an
/// editable card - which is why <see cref="ChatMessage"/> is a closed set of types rather than one
/// type with a kind on it. This is the only place the two meet.
/// </remarks>
public sealed class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? User { get; set; }

    public DataTemplate? Note { get; set; }

    public DataTemplate? Warning { get; set; }

    public DataTemplate? Confirmation { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container) =>
        item switch
        {
            UserMessage => User,
            NoteMessage => Note,
            WarningMessage => Warning,
            ConfirmationCard => Confirmation,
            _ => null
        };
}
