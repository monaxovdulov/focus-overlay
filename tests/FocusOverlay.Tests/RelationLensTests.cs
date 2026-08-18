using FocusOverlay.Core;

namespace FocusOverlay.Tests;

public sealed class RelationLensTests
{
    [Fact]
    public void Selected_connections_stay_full_and_unrelated_connections_dim_to_thirty_percent()
    {
        var selected = Guid.NewGuid();
        var related = new CardConnection
        {
            SourceCardId = selected,
            TargetCardId = Guid.NewGuid()
        };
        var unrelated = new CardConnection
        {
            SourceCardId = Guid.NewGuid(),
            TargetCardId = Guid.NewGuid()
        };

        Assert.Equal(1, RelationLens.ConnectionOpacity(related, selected));
        Assert.Equal(0.3, RelationLens.ConnectionOpacity(unrelated, selected), 6);
        Assert.Equal(1, RelationLens.ConnectionOpacity(unrelated, null));
    }

    [Fact]
    public void Dimming_is_bounded_during_short_lens_transition()
    {
        var unrelated = new CardConnection
        {
            SourceCardId = Guid.NewGuid(),
            TargetCardId = Guid.NewGuid()
        };

        Assert.Equal(1, RelationLens.ConnectionOpacity(unrelated, Guid.NewGuid(), 0));
        Assert.Equal(0.65, RelationLens.ConnectionOpacity(unrelated, Guid.NewGuid(), 0.5), 6);
        Assert.Equal(0.3, RelationLens.ConnectionOpacity(unrelated, Guid.NewGuid(), 2), 6);
    }
}
