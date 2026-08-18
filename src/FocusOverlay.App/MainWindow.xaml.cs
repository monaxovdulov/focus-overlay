using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using FocusOverlay.Core;

namespace FocusOverlay.App;

public partial class MainWindow : Window
{
    private const int WmHotkey = 0x0312;
    private const int FocusHotkeyId = 1;
    private const int ClipboardHotkeyId = 2;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    private readonly Dictionary<Guid, CardWindow> cardWindows = [];
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
        window.Closed += (_, _) =>
        {
            cardWindows.Remove(card.Id);
            UpdateCardCount();
        };
        window.Show();
        window.SetFocusMode(isFocusMode);
        UpdateCardCount();
    }

    public void ToggleFocus()
    {
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
