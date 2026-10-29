using System.Diagnostics;

namespace ApkToPhone;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            return Cli.Run(args);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "APK → телефон",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal static class Cli
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            return TrayHost.Run();
        }

        var command = args[0].TrimStart('-', '/').ToLowerInvariant();

        switch (command)
        {
            case "tray":
                return TrayHost.Run();

            case "install":
            {
                var (serial, path, all) = ParseInstall(args);
                return InstallFlow.Run(serial, path, all);
            }

            case "refresh":
                Refresh();
                return 0;

            case "setup":
                Setup();
                return 0;

            case "uninstall":
                Uninstall();
                return 0;

            case "hint":
                ShowHint();
                return 0;

            case "status":
            {
                var devices = Adb.Devices();
                MessageBox.Show(
                    devices.Count == 0
                        ? "Телефоны не подключены."
                        : string.Join(Environment.NewLine, devices.Select(d => $"{d.DisplayName} — {d.Serial} — {d.State}")),
                    "APK → телефон",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return 0;
            }

            default:
                return TrayHost.Run();
        }
    }

    private static (string? Serial, string? Path, bool All) ParseInstall(string[] args)
    {
        string? serial = null;
        string? path = null;
        var all = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg.Equals("--all", StringComparison.OrdinalIgnoreCase))
            {
                all = true;
                continue;
            }

            if (arg.Equals("--serial", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                serial = args[++i];
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            path ??= arg;
        }

        return (serial, path, all);
    }

    private static void Refresh()
    {
        var adb = Adb.Resolve();
        var devices = adb is null ? Array.Empty<Device>() : Adb.Devices(adb);
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        MenuRegistry.Apply(devices, exe);
    }

    private static void Setup()
    {
        Autostart.Set(true);
        Refresh();
        TrayHost.StartDetached();
    }

    private static void Uninstall()
    {
        MenuRegistry.Remove();
        Autostart.Set(false);

        foreach (var process in Process.GetProcessesByName("ApkToPhone"))
        {
            if (process.Id == Environment.ProcessId)
            {
                continue;
            }

            try
            {
                process.Kill();
            }
            catch (Exception)
            {
                // уже завершился
            }
        }
    }

    private static void ShowHint()
    {
        MessageBox.Show(
            "На телефоне включите «Отладку по USB» (Настройки → Для разработчиков) и подтвердите запрос «Разрешить отладку по USB».\n\n" +
            "Если телефон не появляется: проверьте кабель, режим «Передача файлов» и что на компьютере запущен adb.exe.",
            "APK → телефон",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
