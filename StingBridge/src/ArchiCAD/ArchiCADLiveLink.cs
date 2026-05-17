// StingBridge — ArchiCAD Live Link client.
//
// Communicates with the ArchiCAD Live Connection add-on (shipped with
// ArchiCAD 26+) over a local named-pipe / TCP socket to:
//
//   • Query element properties from the ArchiCAD model without round-tripping
//     through IFC export.
//   • Push STING ISO 19650 tag values into ArchiCAD element properties so
//     the two authoring tools stay in sync.
//   • Trigger an IFC partial-export (just changed elements) on demand.
//
// Connection is optional — STING degrades to the IFC drop-folder path when
// the ArchiCAD service is not reachable.
//
// Protocol: JSON-over-named-pipe (Windows) / JSON-over-Unix-socket (macOS).
// Message envelope: { "cmd": "...", "payload": {...} }
// Supported commands: "ping", "getElement", "setProperty", "exportIfc"

using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json.Linq;
using StingTools.Core;

namespace StingBridge.ArchiCAD
{
    public sealed class ArchiCADLiveLink : IDisposable
    {
        private const string PipeName        = "STING_ARCHICAD_BRIDGE";
        private const int    TimeoutMs      = 3_000;
        private const int    ExportTimeoutMs = 30_000; // ArchiCAD IFC export can take up to 30 s
        private const string ProtocolVersion = "1.0";

        private bool _disposed;

        // Returns true when ArchiCAD Live Connection is running and reachable.
        public bool IsAvailable()
        {
            try
            {
                using var pipe = OpenPipe();
                SendCommand(pipe, "ping", new JObject());
                JObject reply = ReadReply(pipe);
                if (reply.Value<string>("status") != "ok") return false;
                // Warn if version mismatch but still allow connection (graceful degradation).
                var replyVersion = reply.Value<string>("version") ?? "1.0";
                if (replyVersion != ProtocolVersion)
                    StingLog.Warn($"ArchiCADLiveLink: protocol version mismatch — client={ProtocolVersion}, server={replyVersion}. " +
                                  "Some features may not work correctly. Update the ArchiCAD STING add-on.");
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Read a single element's classification data from ArchiCAD.
        public JObject? GetElement(string guidOrId)
        {
            try
            {
                using var pipe = OpenPipe();
                SendCommand(pipe, "getElement", new JObject { ["id"] = guidOrId });
                return ReadReply(pipe);
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ArchiCADLiveLink.GetElement({guidOrId}): {ex.Message}");
                return null;
            }
        }

        // Push a STING parameter value into an ArchiCAD element property.
        public bool SetProperty(string guidOrId, string propertyName, string value)
        {
            try
            {
                using var pipe = OpenPipe();
                SendCommand(pipe, "setProperty", new JObject
                {
                    ["id"]       = guidOrId,
                    ["property"] = propertyName,
                    ["value"]    = value
                });
                JObject reply = ReadReply(pipe);
                return reply.Value<string>("status") == "ok";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ArchiCADLiveLink.SetProperty: {ex.Message}");
                return false;
            }
        }

        // Push multiple STING parameter values into an ArchiCAD element in one round-trip.
        // Returns the count of properties the add-on confirmed it wrote.
        public int BatchSetProperties(string guidOrId, System.Collections.Generic.Dictionary<string, string> properties)
        {
            try
            {
                var propsObj = new JObject();
                foreach (var kv in properties) propsObj[kv.Key] = kv.Value;

                using var pipe = OpenPipe();
                SendCommand(pipe, "batchSetProperty", new JObject
                {
                    ["id"]         = guidOrId,
                    ["properties"] = propsObj
                });
                JObject reply = ReadReply(pipe);
                return reply.Value<string>("status") == "ok"
                    ? (reply.Value<int?>("written") ?? properties.Count)
                    : 0;
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ArchiCADLiveLink.BatchSetProperties: {ex.Message}");
                return 0;
            }
        }

        // Tell ArchiCAD to export changed elements to the IFC drop folder.
        public bool TriggerPartialExport(string dropFolder)
        {
            try
            {
                using var pipe = OpenPipe(ExportTimeoutMs);
                SendCommand(pipe, "exportIfc", new JObject { ["destination"] = dropFolder });
                JObject reply = ReadReply(pipe);
                return reply.Value<string>("status") == "ok";
            }
            catch (Exception ex)
            {
                StingLog.Warn($"ArchiCADLiveLink.TriggerPartialExport: {ex.Message}");
                return false;
            }
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static NamedPipeClientStream OpenPipe(int timeoutMs)
        {
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(timeoutMs);
            return pipe;
        }

        private static NamedPipeClientStream OpenPipe() => OpenPipe(TimeoutMs);

        private static void SendCommand(PipeStream pipe, string cmd, JObject payload)
        {
            var msg = new JObject { ["cmd"] = cmd, ["version"] = ProtocolVersion, ["payload"] = payload };
            byte[] bytes = Encoding.UTF8.GetBytes(msg.ToString(Newtonsoft.Json.Formatting.None) + "\n");
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
        }

        private static JObject ReadReply(PipeStream pipe)
        {
            using var sr = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            string? line = sr.ReadLine();
            return line != null ? JObject.Parse(line) : new JObject { ["status"] = "empty" };
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
