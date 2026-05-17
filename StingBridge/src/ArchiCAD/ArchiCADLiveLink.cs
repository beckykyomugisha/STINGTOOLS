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
        private const string PipeName    = "STING_ARCHICAD_BRIDGE";
        private const int    TimeoutMs   = 3_000;

        private bool _disposed;

        // Returns true when ArchiCAD Live Connection is running and reachable.
        public bool IsAvailable()
        {
            try
            {
                using var pipe = OpenPipe();
                SendCommand(pipe, "ping", new JObject());
                JObject reply = ReadReply(pipe);
                return reply.Value<string>("status") == "ok";
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

        // Tell ArchiCAD to export changed elements to the IFC drop folder.
        public bool TriggerPartialExport(string dropFolder)
        {
            try
            {
                using var pipe = OpenPipe();
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

        private static NamedPipeClientStream OpenPipe()
        {
            var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(TimeoutMs);
            return pipe;
        }

        private static void SendCommand(PipeStream pipe, string cmd, JObject payload)
        {
            var msg = new JObject { ["cmd"] = cmd, ["payload"] = payload };
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
