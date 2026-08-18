namespace FocusOverlay.Core;

public enum CardType
{
    Task,
    Note,
    Image
}

public enum ConnectionDirection
{
    None,
    Forward,
    Backward,
    Both
}

public sealed class FocusCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public CardType Type { get; set; } = CardType.Note;
    public string Title { get; set; } = "Новая карточка";
    public string Content { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public bool ImageFill { get; set; }
    public bool Completed { get; set; }
    public double X { get; set; } = 100;
    public double Y { get; set; } = 100;
    public double Width { get; set; } = 360;
    public double Height { get; set; } = 220;
    public double Opacity { get; set; } = 1;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class CardConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceCardId { get; set; }
    public Guid TargetCardId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Color { get; set; } = "#697BE8";
    public ConnectionDirection Direction { get; set; } = ConnectionDirection.Forward;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public static class RelationLens
{
    public static bool Includes(CardConnection connection, Guid cardId) =>
        connection.SourceCardId == cardId || connection.TargetCardId == cardId;

    public static double ConnectionOpacity(
        CardConnection connection,
        Guid? selectedCardId,
        double transitionProgress = 1)
    {
        if (selectedCardId is null || Includes(connection, selectedCardId.Value))
        {
            return 1;
        }

        return 1 - (0.7 * Math.Clamp(transitionProgress, 0, 1));
    }
}

public interface ICardRepository
{
    Task<IReadOnlyList<FocusCard>> GetAllAsync();
    Task SaveAsync(FocusCard card);
    Task DeleteAsync(Guid id);
    Task<IReadOnlyList<CardConnection>> GetConnectionsAsync();
    Task SaveConnectionAsync(CardConnection connection);
    Task DeleteConnectionAsync(Guid id);
}
