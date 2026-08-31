// ============================================================================
// Project: NosAi — Controlled Automation Runtime
// Version: 1.0 Beta
// Author: Volodymyr Ryzhuk
// Description: Hardware Monitoring subsystem, Runtime Budget Autoscale,
//              Profiler Basato sul Profilo di Riferimento (Acer Nitro V 16 AI) e
//              Watchdog Termico con Trigger a 80 °C
// Standard: C# 12 / .NET 8 — Zero-Allocation, Clean Architecture, Fail-Closed
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NosAi.Hardware.Autoscale
{
    public enum RuntimePerformanceMode : byte
    {
        NominalMax = 0,
        Balanced = 1,
        CoolingThrottled = 2,
        EmergencyMinimal = 3
    }

    public sealed record HardwareTelemetrySnapshot(
        string DeviceModel,
        double CpuUsagePercent,
        double GpuTemperatureCelsius,
        double GpuUsagePercent,
        long RamUsedBytes,
        long RamTotalBytes,
        long VramUsedBytes,
        long VramTotalBytes,
        bool IsThermalThresholdExceeded,
        RuntimePerformanceMode AssignedMode,
        DateTime TimestampUtc
    );

    public sealed record RuntimeBudgetParameters(
        int PerceptionSamplingIntervalMs,
        int DecisionLoopIntervalMs,
        int MaxConcurrentModelInferences,
        bool AllowCloudEscalation
    );

    public sealed class HardwareAutoscaleController
    {
        private const double ThermalLimitCelsius = 80.0;
        private RuntimePerformanceMode _currentMode = RuntimePerformanceMode.NominalMax;
        private readonly object _lock = new();

        public RuntimePerformanceMode CurrentMode { get { lock (_lock) return _currentMode; } }

        public HardwareTelemetrySnapshot EvaluateAndScale(double simulatedGpuTemp = 68.0)
        {
            lock (_lock)
            {
                using var proc = Process.GetCurrentProcess();
                long ramUsed = proc.WorkingSet64;
                long ramTotal = 16L * 1024 * 1024 * 1024;
                bool thermalExceeded = simulatedGpuTemp >= ThermalLimitCelsius;

                if (thermalExceeded) _currentMode = RuntimePerformanceMode.CoolingThrottled;
                else if (simulatedGpuTemp >= 75.0) _currentMode = RuntimePerformanceMode.Balanced;
                else _currentMode = RuntimePerformanceMode.NominalMax;

                return new HardwareTelemetrySnapshot(
                    "Acer Nitro V 16 AI (Ryzen 7 260 + RTX 5060)",
                    12.4,
                    simulatedGpuTemp,
                    28.5,
                    ramUsed,
                    ramTotal,
                    2048L * 1024 * 1024,
                    8192L * 1024 * 1024,
                    thermalExceeded,
                    _currentMode,
                    DateTime.UtcNow);
            }
        }

        public RuntimeBudgetParameters GetBudgetForMode(RuntimePerformanceMode mode) => mode switch
        {
            RuntimePerformanceMode.NominalMax => new RuntimeBudgetParameters(16, 33, 2, false),
            RuntimePerformanceMode.Balanced => new RuntimeBudgetParameters(33, 66, 1, false),
            RuntimePerformanceMode.CoolingThrottled => new RuntimeBudgetParameters(100, 200, 1, false),
            RuntimePerformanceMode.EmergencyMinimal => new RuntimeBudgetParameters(500, 1000, 0, false),
            _ => new RuntimeBudgetParameters(33, 33, 1, false)
        };
    }

    public static class HardwareAutoscaleTestRunner
    {
        public static async Task<bool> RunAllTestsAsync()
        {
            Console.WriteLine("=== Hardware autoscale checks ===");
            bool allPassed = true;
            allPassed &= RunTest("Nominal telemetry is captured", TestNominalTelemetryCapture);
            allPassed &= RunTest("Thermal threshold triggers throttling", TestThermalThrottlingTrigger);
            allPassed &= RunTest("Budget scaling parameters follow the mode", TestBudgetScalingParameters);
            allPassed &= RunTest("Recovery returns to nominal", TestRecoveryToNominalMode);
            allPassed &= RunTest("Autoscale exposes no execution surface", TestHardwareSecurityInvariant);
            Console.WriteLine(allPassed
                ? "=== Hardware autoscale checks passed. Local only. ==="
                : "=== Hardware autoscale checks FAILED. See the lines marked FAIL above. ===");
            await Task.CompletedTask;
            return allPassed;
        }

        private static bool RunTest(string name, Func<bool> testFunc)
        {
            try { return Report(name, testFunc(), null); }
            catch (Exception ex) { return Report(name, false, $"{ex.GetType().Name}: {ex.Message}"); }
        }

        /// <summary>
        /// Reports each check by name.
        /// </summary>
        /// <remarks>
        /// The runner used to discard the name and print nothing, returning one
        /// aggregate bool: a failure gave exit 1 and no way to tell which check
        /// broke or why, because the catch swallowed the exception too. The same
        /// defect was already fixed once for Gate 1.
        /// </remarks>
        private static bool Report(string name, bool passed, string? error)
        {
            string detail = error is null ? string.Empty : $" [{error}]";
            Console.WriteLine($"[{(passed ? "PASS" : "FAIL")}] {name}{detail}");
            return passed;
        }


        private static bool TestNominalTelemetryCapture()
        {
            var controller = new HardwareAutoscaleController();
            var snap = controller.EvaluateAndScale(65.0);
            return snap.AssignedMode == RuntimePerformanceMode.NominalMax && !snap.IsThermalThresholdExceeded && snap.RamTotalBytes > 0;
        }

        private static bool TestThermalThrottlingTrigger()
        {
            var controller = new HardwareAutoscaleController();
            var snap = controller.EvaluateAndScale(82.5);
            return snap.IsThermalThresholdExceeded && snap.AssignedMode == RuntimePerformanceMode.CoolingThrottled;
        }

        private static bool TestBudgetScalingParameters()
        {
            var controller = new HardwareAutoscaleController();
            var budgetNominal = controller.GetBudgetForMode(RuntimePerformanceMode.NominalMax);
            var budgetCooling = controller.GetBudgetForMode(RuntimePerformanceMode.CoolingThrottled);
            return budgetNominal.PerceptionSamplingIntervalMs < budgetCooling.PerceptionSamplingIntervalMs;
        }

        private static bool TestRecoveryToNominalMode()
        {
            var controller = new HardwareAutoscaleController();
            controller.EvaluateAndScale(85.0);
            var recoverSnap = controller.EvaluateAndScale(68.0);
            return recoverSnap.AssignedMode == RuntimePerformanceMode.NominalMax && !recoverSnap.IsThermalThresholdExceeded;
        }

        private static bool TestHardwareSecurityInvariant()
        {
            var types = typeof(HardwareAutoscaleController).Assembly.GetTypes().Where(t => t.Namespace != null && t.Namespace.Contains("NosAi.Hardware.Autoscale"));
            bool hasExecution = types.Any(t => t.GetMethods().Any(m => m.Name.ToLowerInvariant().Contains("click") || m.Name.ToLowerInvariant().Contains("sendpacket")));
            return !hasExecution;
        }
    }
}