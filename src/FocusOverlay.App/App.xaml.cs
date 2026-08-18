using System.Threading;
using System.Windows;
using FocusOverlay.Infrastructure;
using Forms = System.Windows.Forms;

namespace FocusOverlay.App;

public partial class App : System.Windows.Application
{
    private Mutex? singleInstanceMutex;

    public static SqliteCardRepository Repo { get; private set; } = null!;
    public static MainWindow Controller { get; private set; } = null!;
    public static Forms.NotifyIcon Tray { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        singleInstanceMutex = new Mutex(true, @"Local\FocusOverlay.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        try
        {
            Repo = new SqliteCardRepository();
            Controller = new MainWindow();
            Controller.Show();
            Tray = CreateTrayIcon();

            foreach (var card in await Repo.GetAllAsync())
            {
                Controller.AddCard(card);
            }

            Controller.SetConnections(await Repo.GetConnectionsAsync());

            Controller.RegisterHotkeys();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Focus Overlay не удалось запустить.\n\n{exception.Message}",
                "Ошибка запуска",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static Forms.NotifyIcon CreateTrayIcon()
    {
        var tray = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "Focus Overlay"
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть Focus Overlay", null, (_, _) => Controller.ShowController());
        menu.Items.Add("Переключить Focus / Edit", null, (_, _) => Controller.ToggleFocus());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, async (_, _) => await Controller.ExitAsync());

        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Controller.ShowController();
        return tray;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();

        if (singleInstanceMutex is not null)
        {
            try
            {
                singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex can already be released during a failed startup.
            }

            singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
