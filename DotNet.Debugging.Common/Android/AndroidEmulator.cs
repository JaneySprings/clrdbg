using System.Diagnostics;
using DotNet.Debugging.Common.Interop;

namespace DotNet.Debugging.Common.Android;

public static class AndroidEmulator {
    private const int AppearingRetryCount = 120; //seconds

    public static StartResult Run(string name, IProcessLogger? logger = null) {
        var serial = SerialIfRunning(name);
        if (!string.IsNullOrEmpty(serial)) {
            logger?.OnOutputDataReceived($"Skipping the boot of emulator '{name}': it is already running ({serial})");
            return new StartResult(serial, null);
        }

        var emulator = AndroidSdkLocator.GetEmulatorPath();
        var runner = new ProcessRunner(emulator, new ProcessArgumentBuilder()
            .Append("-avd")
            .Append(name));

        logger?.OnOutputDataReceived($"Starting emulator '{name}'");
        var process = runner.Start();
        serial = AndroidEmulator.WaitForBoot();
        logger?.OnOutputDataReceived($"Emulator '{name}' booted successfully ({serial})");

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
        var currentState = AndroidDebugBridge.GetDevices();

        for (int i = 0; i < AppearingRetryCount; i++) {
            Thread.Sleep(1000);
            var newState = AndroidDebugBridge.GetDevices();

            if (newState.Count > currentState.Count) {
                var newSerial = newState.Find(n => !currentState.Any(o => n.Equals(o)));
                if (newSerial != null)
                    return newSerial;
            }
        }
        return null;
    }
    private static string? SerialIfRunning(string avdName) {
        var serials = AndroidDebugBridge.GetDevices().Where(it => it.StartsWith("emulator-"));
        if (serials.Contains(avdName))
            return avdName; // We allow to use avdName property as serial for running devices
        return serials.FirstOrDefault(it => GetEmulatorName(it) == avdName);
    }
    public static string GetEmulatorName(string serial) {
        var adb = AndroidSdkLocator.GetAdbPath();
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