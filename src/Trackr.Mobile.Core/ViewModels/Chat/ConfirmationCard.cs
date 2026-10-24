using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Trackr.Mobile.Core.Nutrition;

namespace Trackr.Mobile.Core.ViewModels.Chat;

/// <summary>Where a card has got to.</summary>
public enum ConfirmationState
{
    /// <summary>Waiting on the user. Nothing has been written.</summary>
    Pending,

    /// <summary>The save is in flight.</summary>
    Saving,

    /// <summary>Written to the log. The card stays in the transcript, read-only.</summary>
    Saved,

    /// <summary>
    /// Approved and stored on the phone, waiting for a server that could not be reached.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="Saved"/>. The numbers are settled and nothing more will be
    /// asked of the user, but today's total does not include this yet and saying "saved to your
    /// log" would be a claim about the server that is not true - see milestone 14's queue.
    /// </remarks>
    Queued,

    /// <summary>Thrown away by the user. Also nothing written.</summary>
    Discarded
}

/// <summary>
/// What the cascade thinks was eaten, offered for correction before anything is stored.
/// </summary>
/// <remarks>
/// The confirm-before-save rule of CLAUDE.md section 2 as a piece of UI: <c>POST /api/analyze</c>
/// writes nothing, and this card is the only thing that turns its answer into a
/// <c>POST /api/log</c>. It stays in the transcript afterwards in whatever state it ended in, so
/// scrolling back shows what was approved rather than a gap where a decision used to be.
/// </remarks>
public sealed partial class ConfirmationCard : ChatMessage
{
    private readonly Func<ConfirmationCard, CancellationToken, Task> save;
    private readonly DisplayUnits units;

    public ConfirmationCard(
        IReadOnlyList<ConfirmableItem> items,
        Func<ConfirmationCard, CancellationToken, Task> save,
        DisplayUnits units)
    {
        Items = items;
        this.save = save;
        this.units = units;

        // Editing any field can turn the confirm button on or off, and the button is bound to the
        // card rather than to the item. Without this the user fixes a typo and the button stays
        // disabled until something else happens to redraw it.
        foreach (var item in items)
        {
            item.PropertyChanged += OnItemChanged;
        }

        ConfirmCommand = new AsyncRelayCommand(ConfirmAsync, () => CanConfirm);
        DiscardCommand = new RelayCommand(Discard, () => State is ConfirmationState.Pending);
    }

    public IReadOnlyList<ConfirmableItem> Items { get; }

    public IAsyncRelayCommand ConfirmCommand { get; }

    public IRelayCommand DiscardCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm))]
    [NotifyPropertyChangedFor(nameof(IsPending))]
    [NotifyPropertyChangedFor(nameof(StateDescription))]
    public partial ConfirmationState State { get; set; } = ConfirmationState.Pending;

    /// <summary>Why the save did not happen. Null unless it failed.</summary>
    [ObservableProperty]
    public partial string? Problem { get; set; }

    public bool IsPending => State is ConfirmationState.Pending;

    public bool CanConfirm => State is ConfirmationState.Pending && Items.All(item => item.IsValid);

    public string? StateDescription => State switch
    {
        ConfirmationState.Saved => "Saved to your log",
        ConfirmationState.Queued => "Saved on this phone - waiting to send",
        ConfirmationState.Discarded => "Discarded - nothing was saved",
        _ => null
    };

    /// <summary>The meal's calories, summed across items. For reading; never sent.</summary>
    /// <remarks>
    /// The server totals the stored entry itself from the same per-serving numbers and quantities,
    /// so this is a second computation of the same thing rather than the source of it. It exists
    /// because "is this meal about right?" is the question the user is actually being asked.
    /// </remarks>
    public string TotalDescription
    {
        get
        {
            var total = Items
                .Where(item => item.IsValid)
                .Sum(item => item.Corrected.Quantity * item.Corrected.EnergyKcal);

            // Corrected is in the unit the API stores, so this converts once for the reader rather
            // than summing figures that have already been converted item by item.
            return units.FormatEnergy(total);
        }
    }

    private async Task ConfirmAsync(CancellationToken cancellationToken)
    {
        Problem = null;
        State = ConfirmationState.Saving;

        try
        {
            await save(this, cancellationToken);
        }
        finally
        {
            // Whoever ran the save sets Saved on success; anything else leaves the card usable so
            // the user can try again rather than losing the numbers they just corrected.
            if (State is ConfirmationState.Saving)
            {
                State = ConfirmationState.Pending;
            }
        }
    }

    private void Discard()
    {
        State = ConfirmationState.Discarded;

        Detach();
    }

    /// <summary>Stops listening to the items once the card can no longer change.</summary>
    public void Detach()
    {
        foreach (var item in Items)
        {
            item.PropertyChanged -= OnItemChanged;
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not nameof(ConfirmableItem.IsValid)
            and not nameof(ConfirmableItem.QuantityText)
            and not nameof(ConfirmableItem.EnergyText))
        {
            return;
        }

        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(TotalDescription));
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnStateChanged(ConfirmationState value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
        DiscardCommand.NotifyCanExecuteChanged();
    }
}
