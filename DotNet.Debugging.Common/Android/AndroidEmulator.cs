using System.Diagnostics;
using System.Text.RegularExpressions;
using DotNet.Debugging.Common.Interop;

namespace DotNet.Debugging.Common.Android;

public static class AndroidEmulator {
    private const int AppearingRetryCount = 120; //seconds

    public static StartResult Run(string name) {
        var rSerial = SerialIfRunning(name);
        if (rSerial != null)
            return new StartResult(rSerial, null);

        var emulator = AndroidSdkLocator.EmulatorTool();
        var runner = new ProcessRunner(emulator, new ProcessArgumentBuilder()
            .Append("-avd")
            .Append(name));

        var process = runner.Start();
        var serial = AndroidEmulator.WaitForBoot();

        return new StartResult(serial, process);
    }

    private static string WaitForBoot() {
        string? serial = WaitForSerial();

        if (serial == null)
            throw new InvalidOperationException("Emulator started but no serial number was found");

        while (!AndroidDebugBridge.Shell(serial, "getprop", "sys.boot_completed").Contains('1'))
            Thread.Sleep(1000);

        return serial;
    }
    private static string? WaitForSerial() {
        var currentState = GetDevices();

        for (int i = 0; i < AppearingRetryCount; i++) {
            Thread.Sleep(1000);
            var newState = GetDevices();

            if (newState.Count > currentState.Count) {
                var newSerial = newState.Find(n => !currentState.Any(o => n.Equals(o)));
                if (newSerial != null)
                    return newSerial;
            }
        }
        return null;
    }
    private static string? SerialIfRunning(string avdName) {
        var serials = GetDevices().Where(it => it.StartsWith("emulator-"));
        if (serials.Contains(avdName))
            return avdName; // We allow to use avdName property as serial for running devices
        return serials.FirstOrDefault(it => GetEmulatorName(it) == avdName);
    }
    private static List<string> GetDevices() {
        var adb = AndroidSdkLocator.AdbTool();
        ProcessResult result = new ProcessRunner(adb, new ProcessArgumentBuilder()
            .Append("devices")
            .Append("-l"))
            .WaitForExit(5000);

        if (!result.Success)
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.StandardError));

        string regex = @"^(?<serial>\S+?)(\s+?)\s+(?<state>\S+)";
        var devices = new List<string>();

        foreach (string line in result.StandardOutput) {
            MatchCollection matches = Regex.Matches(line, regex, RegexOptions.Singleline);
            if (matches.Count == 0)
                continue;

            devices.Add(matches.First().Groups["serial"].Value);
        }

        return devices;
    }
    private static string GetEmulatorName(string serial) {
        var adb = AndroidSdkLocator.AdbTool();
        ProcessResult result = new ProcessRunner(adb, new ProcessArgumentBuilder()
            .Append("-s", serial)
            .Append("emu", "avd", "name"))
            .WaitForExit(5000);

        if (!result.Success)
            return string.Empty;

        return result.StandardOutput.FirstOrDefault() ?? string.Empty;
    }

    public class StartResult {
        public string Serial { get; }
        public Process? Process { get; }

        public StartResult(string serial, Process? process) {
            Serial = serial;
            Process = process;
        }
    }
}