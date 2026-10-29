using System.Diagnostics;
using System.Text;

namespace ApkToPhone;

internal sealed record Device(string Serial, string State, string Model)
{
    public bool IsUsable => string.Equals(State, "device", StringComparison.OrdinalIgnoreCase);

    public string DisplayName => string.IsNullOrWhiteSpace(Model) ? Serial : Model;
}

internal sealed record AdbResult(int ExitCode, string StdOut, string StdErr)
{
    public string All => (StdOut + Environment.NewLine + StdErr).Trim();
}

/// <summary>Тонкая обёртка над adb.exe: поиск, запуск, разбор списка устройств.</summary>
internal static class Adb
{
    private static string? _resolved;

    public static void ResetCache() => _resolved = null;

    public static string? Resolve()
    {
        if (_resolved is not null && File.Exists(_resolved))
        {
            return _resolved;
        }

        foreach (var candidate in Candidates())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            try
            {
                if (File.Exists(candidate))
                {
                    _resolved = candidate;
                    return candidate;
                }
            }
            catch (Exception)
            {
                // некорректный путь в PATH — пропускаем
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates()
    {
        var configured = Config.Load().AdbPath;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            yield return Environment.ExpandEnvironmentVariables(configured);
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = dir.Trim().Trim('"');
            if (trimmed.Length == 0)
            {
                continue;
            }

            string combined;
            try
            {
                combined = Path.Combine(trimmed, "adb.exe");
            }
            catch (Exception)
            {
                continue;
            }

            yield return combined;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, "Android", "Sdk", "platform-tools", "adb.exe");

        yield return @"C:\adb\adb.exe";
        yield return @"C:\platform-tools\adb.exe";
        yield return @"D:\platform-tools\adb.exe";

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
        {
            yield return Path.Combine(home, "platform-tools", "adb.exe");
            yield return Path.Combine(home, "Downloads", "platform-tools", "adb.exe");
        }
    }

    public static AdbResult Run(string adbPath, IEnumerable<string> args, int timeoutMs = 60_000)
    {
        var startInfo = new ProcessStartInfo(adbPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stdout)
                {
                    stdout.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (stderr)
                {
                    stderr.AppendLine(e.Data);
                }
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            return new AdbResult(-1, string.Empty, ex.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(timeoutMs))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // процесс уже завершился
            }

            return new AdbResult(-1, stdout.ToString(), "adb не ответил за отведённое время.");
        }

        process.WaitForExit();
        return new AdbResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }

    public static IReadOnlyList<Device> Devices(string? adbPath = null)
    {
        adbPath ??= Resolve();
        if (adbPath is null)
        {
            return Array.Empty<Device>();
        }

        var result = Run(adbPath, new[] { "devices", "-l" }, 20_000);
        return ParseDevices(result.StdOut);
    }

    internal static IReadOnlyList<Device> ParseDevices(string output)
    {
        var devices = new List<Device>();
        if (string.IsNullOrWhiteSpace(output))
        {
            return devices;
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim('\r', ' ', '\t');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("List of devices", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("adb server", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var serial = parts[0];
            var state = parts[1];
            string? model = null;

            for (var i = 2; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("model:", StringComparison.Ordinal))
                {
                    model = parts[i]["model:".Length..].Replace('_', ' ').Trim();
                }
            }

            devices.Add(new Device(serial, state, string.IsNullOrWhiteSpace(model) ? serial : model!));
        }

        return devices;
    }
}
