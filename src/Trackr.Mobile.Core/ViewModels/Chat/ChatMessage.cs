using CommunityToolkit.Mvvm.ComponentModel;

namespace Trackr.Mobile.Core.ViewModels.Chat;

/// <summary>
/// One thing in the conversation.
/// </summary>
/// <remarks>
/// A closed set of kinds rather than a single message with flags on it, because the four are drawn
/// completely differently - a bubble, a sentence, a warning strip and an editable card - and a
/// template selector needs a type to switch on.
/// <para>
/// <see cref="ObservableObject"/> at the base because two of the four change after they are added:
/// a card is edited and then resolved, and a pending user message gains its analysis. Nothing here
/// is ever removed, so the transcript reads as what actually happened rather than being rewritten.
/// </para>
/// </remarks>
public abstract partial class ChatMessage : ObservableObject;

/// <summary>What the user typed and attached.</summary>
/// <remarks>
/// Photos are held as bytes for drawing, not as ids: the id is what the server needs and the
/// thumbnail is what the transcript needs, and re-downloading a picture the phone just uploaded to
/// draw it back would be a round trip for nothing.
/// </remarks>
public sealed class UserMessage(string? text, IReadOnlyList<byte[]> photos) : ChatMessage
{
    public string? Text { get; } = text;

    public IReadOnlyList<byte[]> Photos { get; } = photos;

    public bool HasText => !string.IsNullOrWhiteSpace(Text);

    public bool HasPhotos => Photos.Count > 0;
}

/// <summary>The model's own sentence about what it did, or the app's about what it is doing.</summary>
public sealed class NoteMessage(string text) : ChatMessage
{
    public string Text { get; } = text;
}

/// <summary>
/// Something the user should know went differently, shown whatever the model said.
/// </summary>
/// <remarks>
/// CLAUDE.md section 5 requires this to be independent of the model's own reply: a rate limit, a
/// timeout or an assumed serving size reaches the user even when the model's sentence never
/// mentions it. Drawn as its own strip for that reason, rather than folded into a note - the point
/// is that it should be hard to read past.
/// </remarks>
public sealed class WarningMessage(string text) : ChatMessage
{
    public string Text { get; } = text;
}
