using System.Text;

namespace ApkToPhone;

internal sealed record InstallOutcome(bool Ok, string Title, string Message);

internal static class InstallFlow
{
    public static int Run(string? serial, string? apkPath, bool all)
    {
        if (string.IsNullOrWhiteSpace(apkPath) || !File.Exists(apkPath))
        {
            MessageBox.Show("Файл APK не найден.", "APK → телефон", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        if (!apkPath.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Выбранный файл не является APK.", "APK → телефон", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return 2;
        }

        using var form = new InstallForm(serial, apkPath, all);
        Application.Run(form);
        return form.ExitCode;
    }

    /// <summary>Установка из трея: один телефон — сразу, несколько — выбор.</summary>
    public static int RunInteractive(string apkPath)
    {
        var devices = Adb.Devices().Where(d => d.IsUsable).ToList();
        if (devices.Count == 0)
        {
            MessageBox.Show(
                "Телефон не подключён.\n\nПодключите кабель, включите «Отладку по USB» и подтвердите запрос на экране телефона.",
                "APK → телефон",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 2;
        }

        if (devices.Count == 1)
        {
            return Run(devices[0].Serial, apkPath, all: false);
        }

        using var chooser = new DeviceChooserForm(devices);
        if (chooser.ShowDialog() != DialogResult.OK || chooser.Selected is null)
        {
            return 0;
        }

        return Run(chooser.Selected.Serial, apkPath, all: false);
    }
}

internal static class Installer
{
    public static InstallOutcome Execute(string? serial, string apkPath, bool all, Action<string> progress)
    {
        var adbPath = Adb.Resolve();
        if (adbPath is null)
        {
            return new InstallOutcome(
                false,
                "adb не найден",
                "Не удалось найти adb.exe.\n\nУстановите Android SDK Platform Tools или пропишите путь к adb.exe в файле:\n" + Config.FilePath);
        }

        var devices = Adb.Devices(adbPath);
        List<Device> targets;

        if (all)
        {
            targets = devices.Where(d => d.IsUsable).ToList();
        }
        else
        {
            var device = devices.FirstOrDefault(d => string.Equals(d.Serial, serial, StringComparison.Ordinal));
            if (device is null)
            {
                return new InstallOutcome(
                    false,
                    "Телефон не подключён",
                    "Устройство не найдено. Проверьте кабель, включённую отладку по USB и разрешение на этом компьютере.");
            }

            if (!device.IsUsable)
            {
                return new InstallOutcome(
                    false,
                    "Нет доступа к телефону",
                    "На экране телефона подтвердите запрос «Разрешить отладку по USB» и повторите установку.");
            }

            targets = new List<Device> { device };
        }

        if (targets.Count == 0)
        {
            return new InstallOutcome(false, "Нет телефона", "Ни один телефон не подключён.");
        }

        var config = Config.Load();
        var extraArguments = config.ExtraInstallArguments().ToList();

        var report = new StringBuilder();
        var failures = new StringBuilder();
        var installed = 0;

        foreach (var device in targets)
        {
            progress($"Установка на {device.DisplayName}…");

            var args = new List<string> { "-s", device.Serial, "install", "-r" };
            args.AddRange(extraArguments);
            args.Add(apkPath);

            var result = Adb.Run(adbPath, args, 10 * 60 * 1000);
            var output = result.All;
            var success = result.ExitCode == 0 && !output.Contains("Failure", StringComparison.OrdinalIgnoreCase);

            if (success)
            {
                installed++;
                report.AppendLine($"✅ {device.DisplayName} — установлено");
            }
            else
            {
                report.AppendLine($"❌ {device.DisplayName} — {Explain(output)}");
                failures.AppendLine($"[{device.DisplayName}]");
                failures.AppendLine(Trim(output, 800));
                failures.AppendLine();
            }
        }

        var ok = installed == targets.Count;
        var message = report.ToString().TrimEnd();

        if (!ok)
        {
            message += string.IsNullOrWhiteSpace(failures.ToString())
                ? string.Empty
                : "\n\nПодробности adb:\n" + failures.ToString().TrimEnd();
        }

        var title = ok
            ? (targets.Count == 1 ? "Установлено" : "Установлено на все устройства")
            : (installed > 0 ? "Установлено частично" : "Не удалось установить");

        // Список устройств мог измениться во время установки — обновляем меню.
        try
        {
            MenuRegistry.Apply(Adb.Devices(adbPath), Environment.ProcessPath ?? string.Empty);
        }
        catch (Exception)
        {
            // меню обновится треем
        }

        return new InstallOutcome(ok, title, message);
    }

    private static string Explain(string output)
    {
        if (output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.Ordinal))
        {
            return "на телефоне уже стоит версия с другой подписью — удалите её и повторите";
        }

        if (output.Contains("INSTALL_FAILED_USER_RESTRICTED", StringComparison.Ordinal))
        {
            return "запрещена установка по USB — включите «Установка через USB» в настройках разработчика";
        }

        if (output.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE", StringComparison.Ordinal))
        {
            return "не хватает места на телефоне";
        }

        if (output.Contains("INSTALL_FAILED_OLDER_SDK", StringComparison.Ordinal))
        {
            return "APK требует более новую версию Android";
        }

        if (output.Contains("INSTALL_FAILED_TEST_ONLY", StringComparison.Ordinal))
        {
            return "APK помечен как test-only";
        }

        if (output.Contains("INSTALL_PARSE_FAILED", StringComparison.Ordinal))
        {
            return "APK повреждён или собран с ошибкой";
        }

        if (output.Contains("device offline", StringComparison.OrdinalIgnoreCase))
        {
            return "телефон отключился от компьютера";
        }

        if (output.Contains("no devices", StringComparison.OrdinalIgnoreCase))
        {
            return "телефон не подключён";
        }

        var line = output
            .Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith("Failure", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(line))
        {
            return line;
        }

        return string.IsNullOrWhiteSpace(output) ? "неизвестная ошибка adb" : Trim(output, 200).ReplaceLineEndings(" ");
    }

    private static string Trim(string text, int max)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}

internal sealed class InstallForm : Form
{
    private readonly string? _serial;
    private readonly string _apkPath;
    private readonly bool _all;
    private readonly Label _status;
    private readonly Label _file;
    private readonly ProgressBar _progress;

    public int ExitCode { get; private set; }

    public InstallForm(string? serial, string apkPath, bool all)
    {
        _serial = serial;
        _apkPath = apkPath;
        _all = all;

        Text = "APK → телефон";
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(480, 132);
        Padding = new Padding(0);

        try
        {
            var exe = Environment.ProcessPath;
            if (exe is not null)
            {
                Icon = Icon.ExtractAssociatedIcon(exe);
            }
        }
        catch (Exception)
        {
            // без иконки
        }

        _status = new Label
        {
            Bounds = new Rectangle(16, 16, 448, 22),
            Text = "Подключаюсь к телефону…",
            AutoEllipsis = true,
        };

        _file = new Label
        {
            Bounds = new Rectangle(16, 44, 448, 20),
            Text = Path.GetFileName(apkPath),
            ForeColor = SystemColors.GrayText,
            AutoEllipsis = true,
        };

        _progress = new ProgressBar
        {
            Bounds = new Rectangle(16, 80, 448, 10),
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25,
        };

        Controls.AddRange(new Control[] { _status, _file, _progress });
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);

        InstallOutcome outcome;
        try
        {
            outcome = await Task.Run(() => Installer.Execute(_serial, _apkPath, _all, Report));
        }
        catch (Exception ex)
        {
            outcome = new InstallOutcome(false, "Ошибка", ex.Message);
        }

        ExitCode = outcome.Ok ? 0 : 1;

        if (!IsDisposed)
        {
            MessageBox.Show(
                this,
                outcome.Message,
                outcome.Title,
                MessageBoxButtons.OK,
                outcome.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        Close();
    }

    private void Report(string text)
    {
        if (IsDisposed)
        {
            return;
        }

        try
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => _status.Text = text));
            }
            else
            {
                _status.Text = text;
            }
        }
        catch (Exception)
        {
            // форма уже закрывается
        }
    }
}

internal sealed class DeviceChooserForm : Form
{
    private readonly ListBox _list;

    public Device? Selected { get; private set; }

    public DeviceChooserForm(IReadOnlyList<Device> devices)
    {
        Text = "Куда установить APK";
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(380, 240);

        _list = new ListBox
        {
            Bounds = new Rectangle(16, 16, 348, 160),
            DisplayMember = nameof(Device.DisplayName),
        };
        foreach (var device in devices)
        {
            _list.Items.Add(device);
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }

        var ok = new Button { Text = "Установить", Bounds = new Rectangle(188, 190, 88, 30), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Отмена", Bounds = new Rectangle(284, 190, 80, 30), DialogResult = DialogResult.Cancel };

        ok.Click += (_, _) => Selected = _list.SelectedItem as Device;
        _list.DoubleClick += (_, _) =>
        {
            Selected = _list.SelectedItem as Device;
            if (Selected is not null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        Controls.AddRange(new Control[] { _list, ok, cancel });
        AcceptButton = ok;
        CancelButton = cancel;
    }
}
