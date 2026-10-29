using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ApkToPhone;

/// <summary>
/// Пункт «Установить на телефон» в контекстном меню .apk-файлов.
/// Всё пишется в HKCU (Software\Classes), поэтому права администратора не нужны.
/// </summary>
internal static class MenuRegistry
{
    private const string VerbKey = @"Software\Classes\SystemFileAssociations\.apk\shell\ApkToPhone";
    private const string SubmenuKey = @"Software\Classes\ApkToPhone.Devices";
    private const string SubmenuClass = "ApkToPhone.Devices";

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfFlush = 0x1000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    public static void NotifyShell()
    {
        try
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfFlush, IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception)
        {
            // Проводник всё равно перечитает меню при следующем открытии
        }
    }

    /// <summary>Перестроить меню под текущий список устройств.</summary>
    public static void Apply(IReadOnlyList<Device> devices, string exePath)
    {
        var usable = devices.Where(d => d.IsUsable).ToList();
        var unauthorized = devices
            .Where(d => !d.IsUsable && d.State.Contains("unauthor", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (usable.Count == 0 && unauthorized.Count == 0)
        {
            Remove();
            return;
        }

        WriteVerb(exePath);
        WriteSubmenu(usable, unauthorized.Count > 0, exePath);
        NotifyShell();
    }

    private static void WriteVerb(string exePath)
    {
        using var verb = Registry.CurrentUser.CreateSubKey(VerbKey, true)
                         ?? throw new InvalidOperationException("Не удалось создать ключ реестра " + VerbKey);

        verb.SetValue(null, string.Empty, RegistryValueKind.String);
        verb.SetValue("MUIVerb", "Установить на телефон", RegistryValueKind.String);
        verb.SetValue("Icon", $"\"{exePath}\",0", RegistryValueKind.String);
        verb.SetValue("SubCommands", string.Empty, RegistryValueKind.String);
        verb.SetValue("ExtendedSubCommandsKey", SubmenuClass, RegistryValueKind.String);
        verb.SetValue("MultiSelectModel", "Single", RegistryValueKind.String);
        verb.SetValue("NeverDefault", string.Empty, RegistryValueKind.String);
    }

    private static void WriteSubmenu(List<Device> usable, bool hasUnauthorized, string exePath)
    {
        Registry.CurrentUser.DeleteSubKeyTree(SubmenuKey, false);

        using var shell = Registry.CurrentUser.CreateSubKey(SubmenuKey + @"\shell", true)
                          ?? throw new InvalidOperationException("Не удалось создать ключ реестра " + SubmenuKey);

        if (usable.Count == 0)
        {
            using var hint = shell.CreateSubKey("unauthorized");
            hint.SetValue(null, string.Empty, RegistryValueKind.String);
            hint.SetValue("MUIVerb", "Подтвердите отладку на телефоне", RegistryValueKind.String);
            hint.SetValue("Icon", $"\"{exePath}\",0", RegistryValueKind.String);
            using var hintCommand = hint.CreateSubKey("command");
            hintCommand.SetValue(null, $"\"{exePath}\" hint", RegistryValueKind.String);
            return;
        }

        var labels = BuildLabels(usable);

        for (var i = 0; i < usable.Count; i++)
        {
            var device = usable[i];
            using var item = shell.CreateSubKey("dev" + i);
            item.SetValue(null, string.Empty, RegistryValueKind.String);
            item.SetValue("MUIVerb", labels[i], RegistryValueKind.String);
            item.SetValue("Icon", $"\"{exePath}\",0", RegistryValueKind.String);
            using var command = item.CreateSubKey("command");
            command.SetValue(null, $"\"{exePath}\" install --serial \"{device.Serial}\" \"%1\"", RegistryValueKind.String);
        }

        if (usable.Count > 1)
        {
            using var item = shell.CreateSubKey("all");
            item.SetValue(null, string.Empty, RegistryValueKind.String);
            item.SetValue("MUIVerb", $"Все устройства ({usable.Count})", RegistryValueKind.String);
            item.SetValue("Icon", $"\"{exePath}\",0", RegistryValueKind.String);
            using var command = item.CreateSubKey("command");
            command.SetValue(null, $"\"{exePath}\" install --all \"%1\"", RegistryValueKind.String);
        }

        if (hasUnauthorized)
        {
            using var item = shell.CreateSubKey("unauthorized");
            item.SetValue(null, string.Empty, RegistryValueKind.String);
            item.SetValue("MUIVerb", "Есть телефон без разрешения — что делать?", RegistryValueKind.String);
            item.SetValue("Icon", $"\"{exePath}\",0", RegistryValueKind.String);
            using var command = item.CreateSubKey("command");
            command.SetValue(null, $"\"{exePath}\" hint", RegistryValueKind.String);
        }
    }

    private static string[] BuildLabels(List<Device> devices)
    {
        var counts = devices
            .GroupBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var labels = new string[devices.Count];
        for (var i = 0; i < devices.Count; i++)
        {
            var device = devices[i];
            labels[i] = counts[device.DisplayName] > 1
                ? $"{device.DisplayName} ({device.Serial})"
                : device.DisplayName;
        }

        return labels;
    }

    /// <summary>Убрать пункт и всё, что с ним связано.</summary>
    public static void Remove()
    {
        var changed = false;

        if (Registry.CurrentUser.OpenSubKey(VerbKey) is not null)
        {
            Registry.CurrentUser.DeleteSubKeyTree(VerbKey, false);
            changed = true;
        }

        if (Registry.CurrentUser.OpenSubKey(SubmenuKey) is not null)
        {
            Registry.CurrentUser.DeleteSubKeyTree(SubmenuKey, false);
            changed = true;
        }

        if (changed)
        {
            NotifyShell();
        }
    }

    public static bool IsInstalled() => Registry.CurrentUser.OpenSubKey(VerbKey) is not null;
}
