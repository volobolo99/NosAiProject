using System;
using System.IO;
using System.Text.Json;
using NosAi.Runtime.Gate3;

namespace NosAi.Runtime.Observability
{
    /// <summary>
    /// Canale 1 di ADR-0030 (C# -> Python, in uscita, solo append): registra ogni
    /// ciclo di decisione Gate3 su un file JSONL per il futuro consumo da parte di
    /// nosai/ Python (evidenze e apprendimento). Solo scrittura, nessuna decisione.
    /// </summary>
    public interface IDecisionTelemetrySink
    {
        void Append(Gate3LoopCycle cycle);
    }

    /// <summary>
    /// Implementazione su file JSONL di <see cref="IDecisionTelemetrySink"/>.
    /// Un fallimento di scrittura non deve mai propagarsi al ciclo di decisione:
    /// viene loggato e ignorato, mai rilanciato.
    /// </summary>
    public sealed class DecisionTelemetryWriter : IDecisionTelemetrySink
    {
        private readonly string _path;
        private readonly IRuntimeLogger _logger;
        private readonly object _lock = new object();

        public DecisionTelemetryWriter(string path, IRuntimeLogger logger)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public void Append(Gate3LoopCycle cycle)
        {
            try
            {
                var directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(new
                {
                    at_utc = cycle.AtUtc,
                    outcome = cycle.Outcome.ToString(),
                    summary = cycle.Summary,
                    selected_action = cycle.SelectedAction.ToString(),
                    would_have_acted = cycle.WouldHaveActed
                });

                lock (_lock)
                {
                    File.AppendAllText(_path, json + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Decision telemetry append failed; the decision loop continues unaffected.", ex);
            }
        }
    }
}
