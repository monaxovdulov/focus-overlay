using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FocusOverlay.Core;
using Point = System.Windows.Point;

namespace FocusOverlay.App;

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private const int FocusHotkeyId = 1;
    private const int ClipboardHotkeyId = 2;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    private readonly Dictionary<Guid, CardWindow> cardWindows = [];
    private readonly List<CardConnection> connections = [];
    private ConnectionOverlayWindow? connectionOverlay;
    private ConnectionEditorWindow? connectionEditor;
    private Guid? connectionDragSource;
    private Guid? highlightedTarget;
    private Guid? relationLensCardId;
    private HwndSource? source;
    private bool isFocusMode;
    private bool isExiting;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            WindowAppearance.Apply(this);
            source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            source?.AddHook(WindowHook);
            connectionOverlay = new ConnectionOverlayWindow();
            connectionOverlay.Show();
            Activate();
        };
        Closing += OnControllerClosing;
        UpdateControllerState();
    }

    public void AddCard(FocusCard card)
    {
        if (cardWindows.ContainsKey(card.Id))
        {
            return;
        }

        var window = new CardWindow(card);
        cardWindows[card.Id] = window;
        window.GeometryChanged += (_, _) => RefreshConnections();
        window.Closed += (_, _) =>
        {
            cardWindows.Remove(card.Id);
            UpdateCardCount();
            RefreshConnections();
        };
        window.Show();
        window.SetFocusMode(isFocusMode);
        UpdateCardCount();
        RefreshConnections();
    }

    public void SetConnections(IEnumerable<CardConnection> restored)
    {
        connections.Clear();
        connections.AddRange(restored.Where(link =>
            cardWindows.ContainsKey(link.SourceCardId) &&
            cardWindows.ContainsKey(link.TargetCardId)));
        RefreshConnections();
    }

    public void BeginConnectionDrag(Guid sourceCardId, Point pointer)
    {
        if (isFocusMode || !cardWindows.TryGetValue(sourceCardId, out var sourceWindow))
        {
            return;
        }

        connectionDragSource = sourceCardId;
        UpdateConnectionDrag(pointer);
        connectionOverlay?.SetLiveConnection(BoundsOf(sourceWindow), pointer, "#697BE8");
    }

    public void UpdateConnectionDrag(Point pointer)
    {
        if (connectionDragSource is not Guid sourceId ||
            !cardWindows.TryGetValue(sourceId, out var sourceWindow))
        {
            return;
        }

        var target = FindConnectionTarget(pointer, sourceId);
        SetHighlightedTarget(target);
        connectionOverlay?.SetLiveConnection(BoundsOf(sourceWindow), pointer, "#697BE8");
    }

    public async Task CompleteConnectionDragAsync(Point pointer)
    {
        if (connectionDragSource is not Guid sourceId)
        {
            return;
        }

        var targetId = FindConnectionTarget(pointer, sourceId);
        CancelConnectionDrag();
        if (targetId is not Guid destinationId)
        {
            return;
        }

        var existing = connections.FirstOrDefault(link =>
            (link.SourceCardId == sourceId && link.TargetCardId == destinationId) ||
            (link.SourceCardId == destinationId && link.TargetCardId == sourceId));
        if (existing is not null)
        {
            connectionOverlay?.Pulse(existing.Id);
            ShowConnections(sourceId, existing.Id);
            return;
        }

        var relation = new CardConnection
        {
            SourceCardId = sourceId,
            TargetCardId = destinationId,
            Color = ConnectionEditorWindow.Palette[connections.Count % ConnectionEditorWindow.Palette.Length],
            Direction = ConnectionDirection.Forward
        };

        try
        {
            await App.Repo.SaveConnectionAsync(relation);
            connections.Add(relation);
            RefreshConnections();
            connectionOverlay?.Pulse(relation.Id);
            if (cardWindows.TryGetValue(sourceId, out var sourceCard))
            {
                sourceCard.ShowConnectionCreatedHint();
            }
        }
        catch (Exception exception)
        {
            ReportWarning($"Не удалось создать связь: {exception.Message}");
        }
    }

    public void CancelConnectionDrag()
    {
        connectionDragSource = null;
        SetHighlightedTarget(null);
        connectionOverlay?.ClearLiveConnection();
    }

    public void ShowConnections(Guid cardId, Guid? selectedRelationId = null)
    {
        if (!cardWindows.TryGetValue(cardId, out var cardWindow))
        {
            return;
        }

        CloseRelationLens();
        relationLensCardId = cardId;
        ApplyRelationLens();
        var editor = new ConnectionEditorWindow(
            SaveConnectionAsync,
            DeleteConnectionAsync,
            RefreshConnections);
        connectionEditor = editor;
        editor.Load(
            cardId,
            connections,
            cardWindows.ToDictionary(pair => pair.Key, pair => pair.Value.Card),
            selectedRelationId);
        editor.Closed += (_, _) =>
        {
            if (ReferenceEquals(connectionEditor, editor))
            {
                connectionEditor = null;
                relationLensCardId = null;
                ApplyRelationLens();
            }
        };
        var cardBounds = BoundsOf(cardWindow);
        var placement = InspectorPlacement(cardId, cardBounds);
        editor.Left = placement.X;
        editor.Top = placement.Y;
        editor.Show();
    }

    public bool CloseRelationLens()
    {
        if (relationLensCardId is null && connectionEditor is null)
        {
            return false;
        }

        var editor = connectionEditor;
        connectionEditor = null;
        relationLensCardId = null;
        ApplyRelationLens();
        editor?.Close();
        return true;
    }

    public async Task DeleteCardAsync(Guid cardId)
    {
        await App.Repo.DeleteAsync(cardId);
        connections.RemoveAll(link =>
            link.SourceCardId == cardId || link.TargetCardId == cardId);
        if (highlightedTarget == cardId || connectionDragSource == cardId)
        {
            CancelConnectionDrag();
        }

        CloseRelationLens();
        RefreshConnections();
    }

    public void ToggleFocus()
    {
        CloseRelationLens();
        var nextMode = !isFocusMode;
        try
        {
            foreach (var window in cardWindows.Values)
            {
                window.SetFocusMode(nextMode);
            }

            isFocusMode = nextMode;
            UpdateControllerState();
            SetWarning(string.Empty);
        }
        catch (Exception exception)
        {
            SetWarning($"Не удалось изменить режим: {exception.Message}");
        }
    }

    public void RegisterHotkeys()
    {
        if (source is null)
        {
            SetWarning("Не удалось подключить глобальные горячие клавиши.");
            return;
        }

        var warnings = new List<string>();
        if (!RegisterHotKey(source.Handle, FocusHotkeyId, ModControl | ModShift, 0x46))
        {
            warnings.Add("Ctrl+Shift+F уже используется другим приложением");
        }

        if (!RegisterHotKey(source.Handle, ClipboardHotkeyId, ModControl | ModShift, 0x20))
        {
            warnings.Add("Ctrl+Shift+Space уже используется другим приложением");
        }

        SetWarning(string.Join(" · ", warnings));
    }

    public void ShowController()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    public async Task ExitAsync()
    {
        if (isExiting)
        {
            return;
        }

        isExiting = true;
        IsEnabled = false;

        await Task.WhenAll(cardWindows.Values.Select(window => window.FlushAsync()));

        if (source is not null)
        {
            UnregisterHotKey(source.Handle, FocusHotkeyId);
            UnregisterHotKey(source.Handle, ClipboardHotkeyId);
            source.RemoveHook(WindowHook);
        }

        foreach (var window in cardWindows.Values.ToArray())
        {
            window.Close();
        }

        connectionEditor?.Close();
        connectionOverlay?.Close();

        App.Tray?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    public void ReportWarning(string message) => SetWarning(message);

    private async Task CreateCardAsync(CardType type, string? content = null, string? imagePath = null)
    {
        var workArea = SystemParameters.WorkArea;
        var offset = (cardWindows.Count % 7) * 26;
        var card = new FocusCard
        {
            Type = type,
            Title = type switch
            {
                CardType.Task => "Новая задача",
                CardType.Image => "Референс",
                _ => "Новая заметка"
            },
            Content = content ?? string.Empty,
            ImagePath = imagePath,
            Width = 360,
            Height = type == CardType.Image ? 260 : 220,
            X = Math.Min(workArea.Right - 340, workArea.Left + 48 + offset),
            Y = Math.Min(workArea.Bottom - 220, workArea.Top + 48 + offset),
            Opacity = 1
        };

        await App.Repo.SaveAsync(card);
        AddCard(card);
    }

    private async void Task_Click(object sender, RoutedEventArgs e) =>
        await CreateCardAsync(CardType.Task);

    private async void Note_Click(object sender, RoutedEventArgs e) =>
        await CreateCardAsync(CardType.Note);

    private async void Image_Click(object sender, RoutedEventArgs e) =>
        await CreateCardAsync(CardType.Image);

    private void Focus_Click(object sender, RoutedEventArgs e) => ToggleFocus();

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private void Help_Click(object sender, RoutedEventArgs e) =>
        HelpPopup.IsOpen = !HelpPopup.IsOpen;

    private async void Exit_Click(object sender, RoutedEventArgs e) => await ExitAsync();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void UpdateControllerState()
    {
        ModeButton.Content = isFocusMode ? "↩" : "⊘";
        ModeButton.ToolTip = isFocusMode
            ? "Снова разрешить взаимодействие с карточками"
            : "Карточки останутся видимыми, но начнут пропускать клики";
        System.Windows.Automation.AutomationProperties.SetName(
            ModeButton,
            isFocusMode ? "Вернуть управление" : "Не мешать");
        ModeDot.Fill = isFocusMode
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(216, 154, 82))
            : (System.Windows.Media.Brush)FindResource("AccentBrush");
    }

    private void UpdateCardCount()
    {
        var count = cardWindows.Count;
        CardCount.Text = count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task SaveConnectionAsync(CardConnection relation)
    {
        await App.Repo.SaveConnectionAsync(relation);
        RefreshConnections();
    }

    private async Task DeleteConnectionAsync(CardConnection relation)
    {
        await App.Repo.DeleteConnectionAsync(relation.Id);
        connections.Remove(relation);
        RefreshConnections();
    }

    private void RefreshConnections()
    {
        connectionOverlay?.UpdateScene(
            connections,
            cardWindows.ToDictionary(pair => pair.Key, pair => BoundsOf(pair.Value)));
    }

    private void ApplyRelationLens()
    {
        connectionOverlay?.SetRelationLens(relationLensCardId);
        foreach (var (id, window) in cardWindows)
        {
            var participates = relationLensCardId is Guid selected &&
                (id == selected || connections.Any(link =>
                    RelationLens.Includes(link, selected) && RelationLens.Includes(link, id)));
            window.SetRelationLens(participates);
        }
    }

    private Point InspectorPlacement(Guid cardId, Rect cardBounds)
    {
        const double gap = 12;
        const double edge = 10;
        var relatedCenters = connections
            .Where(link => link.SourceCardId == cardId || link.TargetCardId == cardId)
            .Select(link => link.SourceCardId == cardId ? link.TargetCardId : link.SourceCardId)
            .Where(cardWindows.ContainsKey)
            .Select(id => BoundsOf(cardWindows[id]))
            .Select(rect => new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2))
            .ToArray();
        var center = new Point(cardBounds.Left + cardBounds.Width / 2, cardBounds.Top + cardBounds.Height / 2);
        var average = relatedCenters.Length == 0
            ? new Point(center.X + 1, center.Y)
            : new Point(relatedCenters.Average(point => point.X), relatedCenters.Average(point => point.Y));

        var below = new Point(cardBounds.Left + 22, cardBounds.Bottom + gap);
        var above = new Point(cardBounds.Left + 22, cardBounds.Top - ConnectionEditorWindow.InspectorHeight - gap);
        var right = new Point(cardBounds.Right + gap, cardBounds.Top);
        var left = new Point(cardBounds.Left - ConnectionEditorWindow.InspectorWidth - gap, cardBounds.Top);
        Point preferred;
        if (Math.Abs(average.X - center.X) >= Math.Abs(average.Y - center.Y))
        {
            preferred = cardBounds.Bottom + gap + ConnectionEditorWindow.InspectorHeight <=
                        SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - edge
                ? below
                : above;
        }
        else
        {
            preferred = average.X >= center.X ? left : right;
        }

        var minX = SystemParameters.VirtualScreenLeft + edge;
        var minY = SystemParameters.VirtualScreenTop + edge;
        var maxX = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth -
                   ConnectionEditorWindow.InspectorWidth - edge;
        var maxY = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight -
                   ConnectionEditorWindow.InspectorHeight - edge;
        return new Point(Math.Clamp(preferred.X, minX, maxX), Math.Clamp(preferred.Y, minY, maxY));
    }

    private Guid? FindConnectionTarget(Point pointer, Guid sourceId) =>
        cardWindows
            .Where(pair => pair.Key != sourceId && BoundsOf(pair.Value).Contains(pointer))
            .Select(pair => (Guid?)pair.Key)
            .FirstOrDefault();

    private void SetHighlightedTarget(Guid? target)
    {
        if (highlightedTarget == target)
        {
            return;
        }

        if (highlightedTarget is Guid previous && cardWindows.TryGetValue(previous, out var oldWindow))
        {
            oldWindow.SetConnectionTargetHighlight(false);
        }

        highlightedTarget = target;
        if (target is Guid next && cardWindows.TryGetValue(next, out var newWindow))
        {
            newWindow.SetConnectionTargetHighlight(true);
        }
    }

    private static Rect BoundsOf(CardWindow window) => new(
        window.Left,
        window.Top,
        Math.Max(window.MinWidth, window.ActualWidth > 0 ? window.ActualWidth : window.Width),
        Math.Max(window.MinHeight, window.ActualHeight > 0 ? window.ActualHeight : window.Height));

    private void SetWarning(string message)
    {
        Warning.Text = message;
        Warning.Visibility = string.IsNullOrWhiteSpace(message)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private IntPtr WindowHook(
        IntPtr handle,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmHotkey)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case FocusHotkeyId:
                ToggleFocus();
                break;
            case ClipboardHotkeyId:
                CreateFromClipboard();
                break;
        }

        handled = true;
        return IntPtr.Zero;
    }

    private async void CreateFromClipboard()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var image = System.Windows.Clipboard.GetImage();
                if (image is not null)
                {
                    var file = ImageInput.SaveClipboardBitmap(image);
                    await CreateCardAsync(CardType.Image, imagePath: file);
                }

                return;
            }

            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                if (ImageInput.TryGetSingleSupportedFile(
                        System.Windows.Clipboard.GetFileDropList().Cast<string>(),
                        out var file))
                {
                    await CreateCardAsync(CardType.Image, imagePath: file);
                }

                return;
            }

            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    await CreateCardAsync(CardType.Note, text);
                }

                return;
            }

            ShowController();
        }
        catch (Exception exception)
        {
            SetWarning($"Не удалось прочитать буфер: {exception.Message}");
            ShowController();
        }
    }

    private void OnControllerClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (isExiting)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr handle, int id);
}
