using System.Collections.ObjectModel;
using AtomUI.Controls;
using ReactiveUI;

namespace AtomUILabsGallery.ShowCases.ScrollMarker;

public sealed class ScrollMarkerViewModel : ReactiveObject, IRoutableViewModel
{
    public static EntityKey ID => "ScrollMarkerShowCase";

    public ScrollMarkerViewModel(IScreen hostScreen)
    {
        HostScreen = hostScreen;
        DirectSections = Enumerable.Range(1, 30)
            .Select(index => new ScrollMarkerDemoSection(
                $"direct-{index:D3}",
                $"Section {index:D2}",
                CreateDirectSectionContent(index)))
            .ToArray();
        HorizontalSections = Enumerable.Range(1, 12)
            .Select(index => new ScrollMarkerDemoSection(
                $"horizontal-{index:D2}",
                $"Horizontal {index:D2}",
                CreateHorizontalSectionContent(index)))
            .ToArray();
        VirtualSections = new ObservableCollection<ScrollMarkerDemoSection>(
            Enumerable.Range(1, 500).Select(index => new ScrollMarkerDemoSection(
                $"virtual-{index:D4}",
                $"Turn {index}",
                index % 5 == 0
                    ? "A deliberately taller answer section.\nIt verifies variable-height estimation and recycling without materializing the entire data source."
                    : "A compact question-and-answer section used to inspect scrolling, active marker follow and container recycling.")));
    }

    public IScreen HostScreen { get; }

    public string UrlPathSegment => ID.ToString();

    public IReadOnlyList<ScrollMarkerDemoSection> DirectSections { get; }

    public IReadOnlyList<ScrollMarkerDemoSection> HorizontalSections { get; }

    public ObservableCollection<ScrollMarkerDemoSection> VirtualSections { get; }

    private static string CreateDirectSectionContent(int index)
    {
        return index switch
        {
            4 => CreateLongDirectSectionContent(index, "stable order across a long section and delayed activation until the following section reaches the anchor line"),
            7 => CreateLongDirectSectionContent(index, "developer-owned Direct content, stable AnchorKey identity and the absence of any required question-and-answer structure"),
            10 => CreateLongDirectSectionContent(index, "large downstream coordinate changes, uniform marker slots and latest-request-wins far navigation"),
            14 => CreateLongDirectSectionContent(index, "variable-height layout, mixed short and long neighbors, and coordinate recalculation after window resizing"),
            18 => CreateLongDirectSectionContent(index, "large scrollbar jumps, final-only automatic selection and isolation between the content and navigator scroll viewers"),
            22 => CreateLongDirectSectionContent(index, "bidirectional far navigation, navigator Browse and Follow, and main-content coordinate conversion"),
            26 => CreateLongDirectSectionContent(index, "late-document hysteresis, superseded requests and remeasurement of heavily wrapped content"),
            29 => CreateLongDirectSectionContent(index, "End-of-content clamping, the penultimate-to-final transition and recovery when scrolling back toward Start"),
            _ => "An independent Direct content section used to inspect marker overflow, active follow and immediate navigation."
        };
    }

    private static string CreateHorizontalSectionContent(int index)
    {
        return index switch
        {
            3 => "This wider card contains extra wrapped text so horizontal navigation can be checked with unequal content density. The card width remains stable while its business content stays developer-owned.",
            7 => "Use the left and right endpoint arrows to move exactly one active marker at a time. The main content should align the neighboring card immediately, without animation or skipped sections.",
            10 => "Drag the horizontal scrollbar back toward the start. Automatic selection should follow the card crossing the logical start anchor while the navigator keeps the active marker visible.",
            _ => $"Horizontal Direct section {index:D2}. Click its marker or scroll the card strip to inspect one-section-to-one-marker navigation."
        };
    }

    private static string CreateLongDirectSectionContent(int index, string focus)
    {
        return $"""
                Section {index:D2} is intentionally much taller than its compact neighbors. Its primary validation focus is {focus}.

                All thirty Direct sections remain materialized because the developer owns their controls and content. The Direct host registers stable anchors, but it does not recycle these business sections when they leave the content viewport.

                Wrapped text changes the arranged height of this card and shifts every later SectionStart. Navigation must consume the latest valid layout instead of retaining a pixel offset calculated before the content was measured.

                Marker slots continue to use stable logical order and a uniform extent. They do not expand to represent this card's height, contract around shorter cards, or become a proportional minimap of content pixels.

                Click this marker from a distant location. The host should resolve the real section start in the main content coordinate space, apply AnchorOffset, clamp the result and update the viewport without playing a smooth animation.

                Scroll slowly across the next boundary in both directions. Small movements inside the four-DIP hysteresis region should not cause the selected marker to flicker between adjacent sections.

                Click two distant markers in quick succession and finish on this one. Only the latest navigation generation may complete; stale requests must not write a late offset or flash intermediate sections across the viewport.

                Resize the Gallery window after arriving here. Rewrapping these paragraphs changes the content extent, but the AnchorKey, descriptor order and one-section-to-one-marker mapping must remain stable.

                Browse the navigator track independently, then resume Follow. The active marker should return to the visible navigator window without forcing the main content to a different semantic section.

                Continue toward the final items and return to this card. End-range clamping must not leave the last marker permanently selected after normal automatic scrolling resumes.
                """;
    }
}

public sealed record ScrollMarkerDemoSection(string AnchorKey, string Label, string Content);
