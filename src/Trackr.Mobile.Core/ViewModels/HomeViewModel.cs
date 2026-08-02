using CommunityToolkit.Mvvm.ComponentModel;

namespace Trackr.Mobile.Core.ViewModels;

/// <summary>
/// Today's totals - the output surface, and the tab the app opens on.
/// </summary>
/// <remarks>
/// Deliberately an honest empty state rather than a dashboard of invented numbers: nothing
/// can be logged until the cascade exists (milestone 9) and there is nothing to aggregate
/// until the data layer does (milestone 6). Milestone 11 fills this in from the nutrient
/// snapshots on LogItems.
/// <para>
/// The date here is for display only. What counts as "today" for an *aggregate* is a server
/// question, because the server aggregates and the day boundary has to follow the account's
/// time zone rather than whichever zone the phone happens to be in - see CLAUDE.md section
/// 9.13. Do not let a phone-local date leak into a total.
/// </para>
/// </remarks>
public sealed partial class HomeViewModel : ObservableObject
{
    public string Today => DateTime.Now.ToString("dddd d MMMM");
}
