namespace FocusOverlay.Core;

public enum CardType
{
    Task,
    Note,
    Image
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

public interface ICardRepository
{
    Task<IReadOnlyList<FocusCard>> GetAllAsync();
    Task SaveAsync(FocusCard card);
    Task DeleteAsync(Guid id);
}
