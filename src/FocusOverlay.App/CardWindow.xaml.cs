using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FocusOverlay.Core;
using Microsoft.Win32;

namespace FocusOverlay.App;

public partial class CardWindow : Window
{
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;

    private readonly FocusCard card;
    private readonly DispatcherTimer saveTimer;
    private readonly DispatcherTimer hintTimer;
    private readonly SemaphoreSlim saveGate = new(1, 1);
    private bool isInitialized;
    private bool isDeleted;
    private bool isFocusMode;
    private bool hasPendingSave;

    public CardWindow(FocusCard card)
    {
        InitializeComponent();
        this.card = card;

        saveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(420)
        };
        saveTimer.Tick += SaveTimer_Tick;

        hintTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1700)
        };
        hintTimer.Tick += (_, _) =>
        {
            hintTimer.Stop();
            AnimateInputHint(false);
        };

        Left = card.X;
        Top = card.Y;
        Width = Math.Max(MinWidth, card.Width);
        Height = Math.Max(MinHeight, card.Height);
        Opacity = 1;
        card.Opacity = 1;

        ConfigureCardType();
        TitleBox.Text = card.Title;
        ContentBox.Text = card.Content;
        TaskDone.IsChecked = card.Completed;

        SourceInitialized += (_, _) =>
        {
            WindowAppearance.Apply(this);
            EnsureVisible();
            ApplyClickThrough();
        };

        isInitialized = true;
    }

    public async Task FlushAsync()
    {
        saveTimer.Stop();
        if (isDeleted)
        {
            return;
        }

        await saveGate.WaitAsync();
        try
        {
            while (hasPendingSave && !isDeleted)
            {
                hasPendingSave = false;
                await App.Repo.SaveAsync(card);
            }
        }
        catch (Exception exception)
        {
            hasPendingSave = true;
            App.Controller.ReportWarning($"Не удалось сохранить карточку: {exception.Message}");
        }
        finally
        {
            saveGate.Release();
        }
    }

    public void SetFocusMode(bool enabled)
    {
        isFocusMode = enabled;
        AnimateEditChrome(!enabled && IsMouseOver);
        CardShell.BorderThickness = new Thickness(1);

        ApplyClickThrough();
    }

    private void ConfigureCardType()
    {
        var accent = card.Type switch
        {
            CardType.Task => System.Windows.Media.Color.FromRgb(105, 123, 232),
            CardType.Image => System.Windows.Media.Color.FromRgb(214, 145, 72),
            _ => System.Windows.Media.Color.FromRgb(73, 169, 132)
        };

        var brush = new SolidColorBrush(accent);
        brush.Freeze();
        TypeDot.Fill = brush;

        TaskDone.Visibility = card.Type == CardType.Task
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (card.Type == CardType.Image)
        {
            ContentBox.Visibility = Visibility.Collapsed;
            ImagePanel.Visibility = Visibility.Visible;
            FillImageButton.Visibility = Visibility.Visible;
            ChooseImageButton.Visibility = Visibility.Visible;
            ApplyImageLayout();
            LoadImage();
        }
    }

    private void LoadImage()
    {
        ImagePreview.Source = null;
        ImagePlaceholder.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(card.ImagePath))
        {
            ImagePlaceholderText.Text = "Ctrl+V или перетащите";
            return;
        }

        if (!File.Exists(card.ImagePath))
        {
            ImagePlaceholderText.Text = "Файл больше недоступен";
            return;
        }

        if (ImageInput.TryDecode(card.ImagePath, out var bitmap))
        {
            ImagePreview.Source = bitmap;
            ImagePlaceholder.Visibility = Visibility.Collapsed;
            return;
        }

        ImagePlaceholderText.Text = "Не удалось открыть изображение";
    }

    private void Text_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (!isInitialized)
        {
            return;
        }

        card.Title = TitleBox.Text;
        card.Content = ContentBox.Text;
        ScheduleSave();
    }

    private void TaskDone_Changed(object sender, RoutedEventArgs e)
    {
        if (!isInitialized)
        {
            return;
        }

        card.Completed = TaskDone.IsChecked == true;
        ScheduleSave();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (!isInitialized)
        {
            return;
        }

        card.X = Left;
        card.Y = Top;
        ScheduleSave();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!isInitialized)
        {
            return;
        }

        card.Width = ActualWidth;
        card.Height = ActualHeight;
        ScheduleSave();
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!isFocusMode && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) =>
        AnimateEditChrome(!isFocusMode);

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) =>
        AnimateEditChrome(false);

    private async void ChooseImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Изображения|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await ApplyImageAsync(dialog.FileName);
    }

    private async void FillImage_Click(object sender, RoutedEventArgs e)
    {
        card.ImageFill = !card.ImageFill;
        card.UpdatedUtc = DateTime.UtcNow;
        ApplyImageLayout();
        hasPendingSave = true;
        await FlushAsync();
    }

    private void ApplyImageLayout()
    {
        var fill = card.Type == CardType.Image && card.ImageFill;
        ContentLayout.Visibility = fill ? Visibility.Collapsed : Visibility.Visible;
        TypeDot.Visibility = fill ? Visibility.Collapsed : Visibility.Visible;
        ImagePanel.Margin = fill ? new Thickness(0) : new Thickness(12, 67, 12, 11);
        ImagePreview.Stretch = fill ? Stretch.UniformToFill : Stretch.Uniform;
        FillImageButton.Content = fill ? "↙" : "⛶";
        FillImageButton.ToolTip = fill
            ? "Вернуть заголовок и поля"
            : "Заполнить карточку изображением";
        System.Windows.Automation.AutomationProperties.SetName(
            FillImageButton,
            fill ? "Вернуть обычный вид изображения" : "Заполнить карточку изображением");
    }

    private void ImagePanel_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (card.Type != CardType.Image || isFocusMode)
        {
            return;
        }

        ImagePanel.Focus();
        Keyboard.Focus(ImagePanel);
    }

    private void PasteImage_CanExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (card.Type == CardType.Image && !isFocusMode && ImagePanel.IsKeyboardFocusWithin)
        {
            e.CanExecute = true;
            e.Handled = true;
        }
    }

    private async void PasteImage_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        e.Handled = true;
        await PasteImageAsync();
    }

    private void Window_PreviewDragEnter(object sender, System.Windows.DragEventArgs e) => UpdateDragCue(e);

    private void Window_PreviewDragOver(object sender, System.Windows.DragEventArgs e) => UpdateDragCue(e);

    private void Window_PreviewDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        DropCue.Visibility = Visibility.Collapsed;
        e.Handled = card.Type == CardType.Image;
    }

    private async void Window_PreviewDrop(object sender, System.Windows.DragEventArgs e)
    {
        DropCue.Visibility = Visibility.Collapsed;
        if (card.Type != CardType.Image || isFocusMode)
        {
            return;
        }

        e.Handled = true;
        if (!ImageInput.CanImportDrop(e.Data))
        {
            ShowInputHint("Перетащите изображение, а не страницу");
            return;
        }

        ShowInputHint("Загружаю изображение…");
        try
        {
            var path = await ImageInput.ImportDropAsync(e.Data);
            await ApplyImageAsync(path);
        }
        catch (InvalidDataException exception)
        {
            ShowInputHint(exception.Message);
        }
        catch (Exception exception)
        {
            ShowInputHint("Не удалось импортировать изображение");
            App.Controller.ReportWarning($"Не удалось импортировать изображение: {exception.Message}");
        }
    }

    private void UpdateDragCue(System.Windows.DragEventArgs e)
    {
        if (card.Type != CardType.Image || isFocusMode)
        {
            e.Effects = System.Windows.DragDropEffects.None;
            DropCue.Visibility = Visibility.Collapsed;
            return;
        }

        var canDrop = ImageInput.CanImportDrop(e.Data);
        e.Effects = canDrop
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        DropCue.Visibility = canDrop ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private async Task PasteImageAsync()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var bitmap = System.Windows.Clipboard.GetImage();
                if (bitmap is not null)
                {
                    var path = ImageInput.SaveClipboardBitmap(bitmap);
                    await ApplyImageAsync(path);
                    return;
                }
            }

            if (System.Windows.Clipboard.ContainsFileDropList() &&
                ImageInput.TryGetSingleSupportedFile(
                    System.Windows.Clipboard.GetFileDropList().Cast<string>(),
                    out var file))
            {
                await ApplyImageAsync(file);
                return;
            }

            var data = System.Windows.Clipboard.GetDataObject();
            if (data is not null && ImageInput.CanImportDrop(data))
            {
                ShowInputHint("Загружаю изображение…");
                var path = await ImageInput.ImportDropAsync(data);
                await ApplyImageAsync(path);
                return;
            }

            ShowInputHint("В буфере нет изображения");
        }
        catch (InvalidDataException exception)
        {
            ShowInputHint(exception.Message);
        }
        catch (Exception exception)
        {
            ShowInputHint("Не удалось вставить изображение");
            App.Controller.ReportWarning($"Не удалось вставить изображение: {exception.Message}");
        }
    }

    private async Task ApplyImageAsync(string path)
    {
        if (!ImageInput.TryDecode(path, out var bitmap))
        {
            ShowInputHint("Не удалось открыть изображение");
            return;
        }

        card.ImagePath = Path.GetFullPath(path);
        card.UpdatedUtc = DateTime.UtcNow;
        ImagePreview.Source = bitmap;
        ImagePlaceholder.Visibility = Visibility.Collapsed;
        hasPendingSave = true;
        await FlushAsync();
        ShowInputHint("Изображение вставлено");
    }

    private void ShowInputHint(string message)
    {
        InputHintText.Text = message;
        hintTimer.Stop();
        AnimateInputHint(true);
        hintTimer.Start();
    }

    private void AnimateInputHint(bool show)
    {
        var animation = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(show ? 120 : 220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        InputHint.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        isDeleted = true;
        hasPendingSave = false;
        saveTimer.Stop();

        try
        {
            await App.Repo.DeleteAsync(card.Id);
            Close();
        }
        catch (Exception exception)
        {
            isDeleted = false;
            App.Controller.ReportWarning($"Не удалось удалить карточку: {exception.Message}");
        }
    }

    private void ScheduleSave()
    {
        card.UpdatedUtc = DateTime.UtcNow;
        hasPendingSave = true;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private async void SaveTimer_Tick(object? sender, EventArgs e)
    {
        saveTimer.Stop();
        await FlushAsync();
    }

    private void AnimateEditChrome(bool show)
    {
        EditChrome.IsHitTestVisible = show;

        var animation = new DoubleAnimation
        {
            To = show ? 1 : 0,
            Duration = TimeSpan.FromMilliseconds(show ? 160 : 240),
            EasingFunction = new QuadraticEase
            {
                EasingMode = show ? EasingMode.EaseOut : EasingMode.EaseInOut
            },
            FillBehavior = FillBehavior.HoldEnd
        };

        EditChrome.BeginAnimation(
            OpacityProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void EnsureVisible()
    {
        const double visibleMargin = 48;
        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualRight = virtualLeft + SystemParameters.VirtualScreenWidth;
        var virtualBottom = virtualTop + SystemParameters.VirtualScreenHeight;

        var intersects = Left + Width > virtualLeft + visibleMargin &&
                         Left < virtualRight - visibleMargin &&
                         Top + Height > virtualTop + visibleMargin &&
                         Top < virtualBottom - visibleMargin;

        if (intersects)
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        Left = workArea.Left + 48;
        Top = workArea.Top + 48;
        card.X = Left;
        card.Y = Top;
        ScheduleSave();
    }

    private void ApplyClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var extendedStyle = GetWindowLong(handle, GwlExstyle);
        var nextStyle = isFocusMode
            ? extendedStyle | WsExTransparent
            : extendedStyle & ~WsExTransparent;

        if (nextStyle != extendedStyle)
        {
            _ = SetWindowLong(handle, GwlExstyle, nextStyle);
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr handle, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr handle, int index, int newStyle);
}
