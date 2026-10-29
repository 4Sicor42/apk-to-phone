using System.Diagnostics;

namespace ApkToPhone;

internal static class TrayHost
{
    private const string MutexName = @"Local\ApkToPhone.Tray.v1";

    public static bool IsRunning()
    {
        try
        {
            if (Mutex.TryOpenExisting(MutexName, out var existing))
            {
                existing.Dispose();
                return true;
            }
        }
        catch (Exception)
        {
            // игнорируем
        }

        return false;
    }

    public static int Run()
    {
        using var mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            return 0;
        }

        Application.Run(new TrayContext());
        GC.KeepAlive(mutex);
        return 0;
    }

    public static void StartDetached()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || IsRunning())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(exe)
            {
                Arguments = "tray",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch (Exception)
        {
            // не удалось — не критично
        }
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly ToolStripMenuItem _autostartItem;
    private readonly string _exePath;
    private string _signature = "\0";
    private bool _checking;
    private bool _firstTick = true;

    public TrayContext()
    {
        _exePath = Environment.ProcessPath ?? Application.ExecutablePath;

        var menu = new ContextMenuStrip();

        var installItem = new ToolStripMenuItem("Установить APK на телефон…");
        installItem.Click += (_, _) => PickAndInstall();

        var refreshItem = new ToolStripMenuItem("Обновить меню");
        refreshItem.Click += (_, _) => CheckDevices(force: true);

        _autostartItem = new ToolStripMenuItem("Запускать при входе")
        {
            CheckOnClick = true,
            Checked = Autostart.IsEnabled(),
        };
        _autostartItem.Click += (_, _) => Autostart.Set(_autostartItem.Checked);

        var folderItem = new ToolStripMenuItem("Открыть папку программы");
        folderItem.Click += (_, _) => OpenFolder();

        var settingsItem = new ToolStripMenuItem("Настройки (путь к adb)…");
        settingsItem.Click += (_, _) => OpenSettings();

        var exitItem = new ToolStripMenuItem("Выход");
        exitItem.Click += (_, _) => ExitThread();

        menu.Items.AddRange(new ToolStripItem[]
        {
            installItem,
            refreshItem,
            new ToolStripSeparator(),
            _autostartItem,
            folderItem,
            settingsItem,
            new ToolStripSeparator(),
            exitItem,
        });

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "APK → телефон",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => PickAndInstall();

        _timer = new System.Windows.Forms.Timer { Interval = 400 };
        _timer.Tick += OnTimerTick;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_firstTick)
        {
            _firstTick = false;
            _timer.Interval = 4000;
            CheckDevices(force: true);
            return;
        }

        CheckDevices();
    }

    private void CheckDevices(bool force = false)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        var ui = SynchronizationContext.Current;

        Task.Run(() =>
        {
            var adb = Adb.Resolve();
            var devices = adb is null ? Array.Empty<Device>() : Adb.Devices(adb);
            var signature = string.Join("|", devices.Select(d => d.Serial + ":" + d.State + ":" + d.Model));
            return (Devices: devices, Signature: signature);
        }).ContinueWith(
            task =>
            {
                _checking = false;
                if (task.IsFaulted)
                {
                    return;
                }

                var (devices, signature) = task.Result;
                if (force || !string.Equals(signature, _signature, StringComparison.Ordinal))
                {
                    _signature = signature;
                    try
                    {
                        MenuRegistry.Apply(devices, _exePath);
                    }
                    catch (Exception)
                    {
                        // нет прав на реестр — молча пропускаем
                    }
                }

                UpdateTooltip(devices);
            },
            ui is null ? TaskScheduler.Default : TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void UpdateTooltip(IReadOnlyList<Device> devices)
    {
        var usable = devices.Where(d => d.IsUsable).ToList();

        var text = usable.Count switch
        {
            0 => "APK → телефон: нет устройства",
            1 => "APK → телефон: " + usable[0].DisplayName,
            _ => "APK → телефон: устройств " + usable.Count,
        };

        if (text.Length > 63)
        {
            text = text[..63];
        }

        _notifyIcon.Text = text;
    }

    private void PickAndInstall()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите APK-файл",
            Filter = "Android-пакет (*.apk)|*.apk|Все файлы (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            InstallFlow.RunInteractive(dialog.FileName);
        }
    }

    private static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Config.DirectoryPath);
            Process.Start(new ProcessStartInfo(Config.DirectoryPath) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // игнорируем
        }
    }

    private static void OpenSettings()
    {
        try
        {
            var config = Config.Load();
            config.Save();
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{Config.FilePath}\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // игнорируем
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                var icon = Icon.ExtractAssociatedIcon(exe);
                if (icon is not null)
                {
                    return icon;
                }
            }
        }
        catch (Exception)
        {
            // fallback ниже
        }

        return SystemIcons.Application;
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}
