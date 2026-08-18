using System.IO;
using FocusOverlay.Core;
using FocusOverlay.Infrastructure;

namespace FocusOverlay.Tests;

public class RepositoryTests
{
    [Fact]
    public async Task CrudRoundTripPersistsGeometryAndContent()
    {
        var path = NewDatabasePath();
        var repository = new SqliteCardRepository(path);
        var card = new FocusCard
        {
            Type = CardType.Note,
            Title = "Тест",
            Content = "Текст",
            X = 42,
            Y = 77,
            Width = 410,
            Height = 220,
            Opacity = 0.6,
            ImageFill = true
        };

        await repository.SaveAsync(card);
        var loaded = Assert.Single(await repository.GetAllAsync());

        Assert.Equal(card.Id, loaded.Id);
        Assert.Equal(card.Content, loaded.Content);
        Assert.Equal(card.X, loaded.X);
        Assert.Equal(card.Opacity, loaded.Opacity);
        Assert.True(loaded.ImageFill);

        await repository.DeleteAsync(card.Id);
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task ConcurrentWritesAreSerializedWithoutDatabaseLocks()
    {
        var repository = new SqliteCardRepository(NewDatabasePath());
        var cards = Enumerable.Range(0, 24)
            .Select(index => new FocusCard
            {
                Title = $"Карточка {index}",
                Content = new string('x', 100 + index)
            })
            .ToArray();

        await Task.WhenAll(cards.Select(repository.SaveAsync));

        var loaded = await repository.GetAllAsync();
        Assert.Equal(cards.Length, loaded.Count);
        Assert.Equal(
            cards.Select(card => card.Id).Order(),
            loaded.Select(card => card.Id).Order());
    }

    [Fact]
    public void DefaultsAreUseful()
    {
        var card = new FocusCard();

        Assert.Equal(CardType.Note, card.Type);
        Assert.True(card.Width >= 240);
        Assert.True(card.Height >= 180);
        Assert.InRange(card.Opacity, 0.35, 1);
    }

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"focus-{Guid.NewGuid():N}.db");
}
