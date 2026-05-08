// Healthcare Pack H-20 — IoT registry inspect command.
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using StingTools.Core;
using StingTools.Core.Twin;
using System;
using System.Linq;
using System.Text;

namespace StingTools.Commands.Twin
{
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    public class IoTRegistryCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                var doc = commandData.Application.ActiveUIDocument.Document;
                var reg = new IoTDeviceRegistry(doc);
                var all = reg.All().ToList();
                var sb = new StringBuilder();
                sb.AppendLine("STING — IoT Device Registry").AppendLine();
                sb.AppendLine($"Devices registered: {all.Count}");
                foreach (var grp in all.GroupBy(d => d.Protocol).OrderBy(g => g.Key))
                    sb.AppendLine($"  {grp.Key,-12}  {grp.Count(),4}");
                sb.AppendLine();
                foreach (var d in all.Take(40))
                    sb.AppendLine($"  {d.DeviceId,-30} {d.Protocol,-10} {d.EndpointAddress}");
                if (all.Count > 40) sb.AppendLine($"  ... + {all.Count - 40} more (see StingTools.log)");
                StingLog.Info(sb.ToString());
                TaskDialog.Show("STING — IoT Registry", sb.ToString());
                return Result.Succeeded;
            }
            catch (Exception ex) { StingLog.Error("IoTRegistryCommand failed", ex); message = ex.Message; return Result.Failed; }
        }
    }
}
