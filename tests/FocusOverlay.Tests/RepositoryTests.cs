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

    [Fact]
    public async Task ConnectionRoundTripPersistsLabelColorAndDirection()
    {
        var repository = new SqliteCardRepository(NewDatabasePath());
        var source = new FocusCard { Title = "Идея" };
        var target = new FocusCard { Title = "Действие" };
        await repository.SaveAsync(source);
        await repository.SaveAsync(target);
        var relation = new CardConnection
        {
            SourceCardId = source.Id,
            TargetCardId = target.Id,
            Label = "приводит к",
            Color = "#49A984",
            Direction = ConnectionDirection.Both
        };

        await repository.SaveConnectionAsync(relation);
        var loaded = Assert.Single(await repository.GetConnectionsAsync());

        Assert.Equal(relation.Id, loaded.Id);
        Assert.Equal("приводит к", loaded.Label);
        Assert.Equal("#49A984", loaded.Color);
        Assert.Equal(ConnectionDirection.Both, loaded.Direction);
    }

    [Fact]
    public async Task DeletingCardAlsoDeletesItsConnections()
    {
        var repository = new SqliteCardRepository(NewDatabasePath());
        var source = new FocusCard();
        var target = new FocusCard();
        await repository.SaveAsync(source);
        await repository.SaveAsync(target);
        await repository.SaveConnectionAsync(new CardConnection
        {
            SourceCardId = source.Id,
            TargetCardId = target.Id
        });

        await repository.DeleteAsync(source.Id);

        Assert.Empty(await repository.GetConnectionsAsync());
        Assert.Single(await repository.GetAllAsync());
    }

    private static string NewDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"focus-{Guid.NewGuid():N}.db");
}
