using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using FocusOverlay.Core;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Cursors = System.Windows.Input.Cursors;
using Ellipse = System.Windows.Shapes.Ellipse;
using FontFamily = System.Windows.Media.FontFamily;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace FocusOverlay.App;

public sealed class ConnectionEditorWindow : Window
{
    public const double InspectorWidth = 270;
    public const double InspectorHeight = 210;
    public static readonly string[] Palette =
        ["#3979E9", "#41AA7B", "#E89718", "#8A55D0", "#EF5049"];

    private readonly Grid body = new();
    private readonly Func<CardConnection, Task> save;
    private readonly Func<CardConnection, Task> delete;
    private readonly Action changed;
    private IReadOnlyDictionary<Guid, FocusCard> cards = new Dictionary<Guid, FocusCard>();
    private Guid cardId;
    private List<CardConnection> relations = [];
    private CardConnection? selected;

    public ConnectionEditorWindow(
        Func<CardConnection, Task> save,
        Func<CardConnection, Task> delete,
        Action changed)
    {
        this.save = save;
        this.delete = delete;
        this.changed = changed;
        Width = InspectorWidth;
        Height = InspectorHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;

        var close = SymbolButton("×", "Закрыть Relation Lens", 28);
        close.FontSize = 18;
        close.Click += (_, _) => Close();
        var header = new Grid { Margin = new Thickness(16, 11, 10, 6) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Связь",
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("#252725"),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(header);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Content = new Border
        {
            Background = Brush("#FAF9F6"),
            BorderBrush = Brush("#D7D4CD"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Effect = new DropShadowEffect
            {
                BlurRadius = 20,
                ShadowDepth = 5,
                Opacity = 0.19,
                Color = Color.FromRgb(62, 58, 52)
            },
            Child = root
        };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }
        };
    }

    public void Load(
        Guid selectedCardId,
        IReadOnlyList<CardConnection> allRelations,
        IReadOnlyDictionary<Guid, FocusCard> allCards,
        Guid? selectedRelationId = null)
    {
        cardId = selectedCardId;
        relations = allRelations
            .Where(link => link.SourceCardId == cardId || link.TargetCardId == cardId)
            .ToList();
        cards = allCards;
        selected = relations.FirstOrDefault(link => link.Id == selectedRelationId) ?? relations.FirstOrDefault();
        Rebuild();
    }

    private void Rebuild()
    {
        body.Children.Clear();
        body.RowDefinitions.Clear();
        if (relations.Count == 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "Потяните синюю точку к другой карточке",
                Margin = new Thickness(16, 14, 16, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#7D7F7A"),
                FontSize = 12
            });
            return;
        }

        if (relations.Count > 1)
        {
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            body.RowDefinitions.Add(new RowDefinition());
            var list = new ScrollViewer
            {
                Margin = new Thickness(12, 0, 12, 5),
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = RelationList()
            };
            body.Children.Add(list);
        }
        else
        {
            body.RowDefinitions.Add(new RowDefinition());
        }

        if (selected is not null)
        {
            var editor = RelationEditor(selected);
            Grid.SetRow(editor, body.RowDefinitions.Count - 1);
            body.Children.Add(editor);
        }
    }

    private UIElement RelationList()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var relation in relations)
        {
            var button = new Button
            {
                Content = RelationSummary(relation),
                Height = 34,
                MinWidth = 108,
                MaxWidth = 205,
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(0, 0, 6, 0),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                Background = Brush(relation == selected ? "#F0F2F5" : "#FAF9F6"),
                BorderBrush = Brush(relation == selected ? relation.Color : "#DEDBD4"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = RelationNames(relation)
            };
            button.Click += (_, _) =>
            {
                selected = relation;
                Rebuild();
            };
            panel.Children.Add(button);
        }
        return panel;
    }

    private UIElement RelationSummary(CardConnection relation)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = Brush(relation.Color),
            Margin = new Thickness(0, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = OtherTitle(relation),
            MaxWidth = 155,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11.5,
            Foreground = Brush("#353735"),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private UIElement RelationEditor(CardConnection relation)
    {
        var stack = new StackPanel { Margin = new Thickness(12, 0, 12, 9) };
        stack.Children.Add(new Border
        {
            Height = 34,
            Padding = new Thickness(10, 0, 10, 0),
            Background = Brush("#F7F6F3"),
            BorderBrush = Brush("#DEDCD6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Child = RelationHeading(relation)
        });

        var label = new TextBox
        {
            Text = relation.Label,
            Height = 32,
            Margin = new Thickness(0, 6, 0, 6),
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
            Foreground = Brush("#333533"),
            Background = Brush("#FCFBF8"),
            BorderBrush = Brush("#D9D7D1"),
            BorderThickness = new Thickness(1),
            ToolTip = "Подпись — необязательно"
        };
        System.Windows.Automation.AutomationProperties.SetName(label, "Подпись связи");
        label.TextChanged += (_, _) =>
        {
            relation.Label = label.Text;
            changed();
        };
        label.LostKeyboardFocus += async (_, _) => await SaveSafely(relation);
        label.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await SaveSafely(relation);
                Keyboard.ClearFocus();
            }
        };
        stack.Children.Add(label);

        var controls = new Grid();
        controls.ColumnDefinitions.Add(new ColumnDefinition());
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        controls.Children.Add(ColorPalette(relation));
        var directions = DirectionSelector(relation);
        Grid.SetColumn(directions, 1);
        controls.Children.Add(directions);
        var more = SymbolButton("•••", "Меню связи", 34);
        more.Margin = new Thickness(7, 0, 0, 0);
        more.BorderBrush = Brush("#DAD8D2");
        more.BorderThickness = new Thickness(1);
        var menu = new ContextMenu();
        var remove = new MenuItem { Header = "Удалить связь", Foreground = Brush("#B4413D") };
        remove.Click += async (_, _) => await DeleteSafely(relation);
        menu.Items.Add(remove);
        more.ContextMenu = menu;
        more.Click += (_, _) =>
        {
            more.ContextMenu.PlacementTarget = more;
            more.ContextMenu.IsOpen = true;
        };
        Grid.SetColumn(more, 2);
        controls.Children.Add(more);
        stack.Children.Add(controls);
        return stack;
    }

    private UIElement RelationHeading(CardConnection relation)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = Brush(relation.Color),
            Margin = new Thickness(0, 0, 9, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        var text = new TextBlock
        {
            Text = RelationNames(relation),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 11.5,
            Foreground = Brush("#303230"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private UIElement ColorPalette(CardConnection relation)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var color in Palette)
        {
            var selectedColor = color == relation.Color;
            var dot = new Button
            {
                Width = 18,
                Height = 30,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Цвет связи",
                Content = new Border
                {
                    Width = selectedColor ? 13 : 11,
                    Height = selectedColor ? 13 : 11,
                    CornerRadius = new CornerRadius(7),
                    Background = Brush(color),
                    BorderBrush = selectedColor ? Brush("#FFFFFF") : Brushes.Transparent,
                    BorderThickness = new Thickness(selectedColor ? 2 : 0),
                    Effect = selectedColor
                        ? new DropShadowEffect { BlurRadius = 5, ShadowDepth = 0, Opacity = 0.45, Color = (Color)ColorConverter.ConvertFromString(color) }
                        : null
                }
            };
            dot.Click += async (_, _) =>
            {
                relation.Color = color;
                changed();
                await SaveSafely(relation);
                Rebuild();
            };
            panel.Children.Add(dot);
        }
        return panel;
    }

    private UIElement DirectionSelector(CardConnection relation)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var item in new[]
                 {
                     (ConnectionDirection.None, "—"),
                     (ConnectionDirection.Forward, "→"),
                     (ConnectionDirection.Backward, "←"),
                     (ConnectionDirection.Both, "↔")
                 })
        {
            var button = SymbolButton(item.Item2, "Направление связи", 26);
            button.Height = 30;
            button.Margin = new Thickness(-1, 0, 0, 0);
            button.Foreground = item.Item1 == relation.Direction ? Brush(relation.Color) : Brush("#565854");
            button.Background = item.Item1 == relation.Direction ? Brush("#EEF2FA") : Brush("#FAF9F6");
            button.BorderBrush = Brush("#DAD8D2");
            button.BorderThickness = new Thickness(1);
            button.Click += async (_, _) =>
            {
                relation.Direction = item.Item1;
                changed();
                await SaveSafely(relation);
                Rebuild();
            };
            panel.Children.Add(button);
        }
        return panel;
    }

    private async Task SaveSafely(CardConnection relation)
    {
        try
        {
            await save(relation);
        }
        catch (Exception exception)
        {
            App.Controller.ReportWarning($"Не удалось сохранить связь: {exception.Message}");
        }
    }

    private async Task DeleteSafely(CardConnection relation)
    {
        try
        {
            await delete(relation);
            relations.Remove(relation);
            selected = relations.FirstOrDefault();
            changed();
            if (selected is null)
            {
                Close();
            }
            else
            {
                Rebuild();
            }
        }
        catch (Exception exception)
        {
            App.Controller.ReportWarning($"Не удалось удалить связь: {exception.Message}");
        }
    }

    private string RelationNames(CardConnection relation)
    {
        var source = cards.TryGetValue(relation.SourceCardId, out var sourceCard) ? sourceCard.Title : "Карточка";
        var target = cards.TryGetValue(relation.TargetCardId, out var targetCard) ? targetCard.Title : "Карточка";
        return $"{source}  {DirectionSymbol(relation.Direction)}  {target}";
    }

    private string OtherTitle(CardConnection relation)
    {
        var otherId = relation.SourceCardId == cardId ? relation.TargetCardId : relation.SourceCardId;
        return cards.TryGetValue(otherId, out var card) ? card.Title : "Карточка";
    }

    private static Button SymbolButton(string symbol, string tooltip, double width) => new()
    {
        Content = symbol,
        Width = width,
        Height = 28,
        Padding = new Thickness(0),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Foreground = Brush("#72746F"),
        FontSize = 13,
        Cursor = Cursors.Hand,
        ToolTip = tooltip
    };

    private static string DirectionSymbol(ConnectionDirection direction) => direction switch
    {
        ConnectionDirection.None => "—",
        ConnectionDirection.Backward => "←",
        ConnectionDirection.Both => "↔",
        _ => "→"
    };

    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
