// Licensed to Planscape under the STING Tools plug-in LICENSE. See LICENSE.
//
// StingTools/V6/AccFileIsoFacts.cs
//
// "What suitability, revision and document number does THIS file carry?" — answered for an
// ACC upload of a file the operator picked (ACC_UploadModel) or the bundle ACC Publish built
// (ACC_UploadLastBundle), from the only two places that know: the bundle record, and the
// document register row the file was recorded under. Before this, the upload sent
// Revision = "" for every file and the document number was the file name.
//
// It also decides when a file must NOT go: a name carrying the export not-set markers
// (ExportIsoFields — "…-XX-NOREV"), or a register row flagged iso_unset by the Export Centre.
// Uploading such a file would put a deliverable into an issued CDE container under a status
// or revision nobody set.
//
// Revit-free and log-free: linked into StingTools.Acc.Tests.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using StingTools.Core;
using StingTools.Core.Drawing;

namespace StingTools.V6
{
    public sealed class AccFileIsoFacts
    {
        /// <summary>ISO document number: the register's doc_number, else the file name stem.</summary>
        public string DocumentNumber { get; set; } = string.Empty;
        /// <summary>Suitability code; empty when nothing records one (the caller may ask).</summary>
        public string Suitability { get; set; } = string.Empty;
        /// <summary>Revision label; empty when nothing records one — sent as unset, never guessed.</summary>
        public string Revision { get; set; } = string.Empty;
        /// <summary>Set when the file must not be uploaded; the sentence says why.</summary>
        public string Refusal { get; set; }
        public bool Refused => !string.IsNullOrEmpty(Refusal);
        /// <summary>Where each value came from, and anything left unset — for the dialog.</summary>
        public List<string> Notes { get; } = new List<string>();
        /// <summary>The register row the file matched, if any.</summary>
        public JObject RegisterRow { get; set; }
    }

    public static class AccFileIso
    {
        /// <summary>
        /// The register row recorded for this file: an exact file_path match (case-insensitive,
        /// normalised), else a file_name match ONLY when exactly one row has that name — two
        /// rows named alike are two documents, and picking one would be a guess.
        /// </summary>
        public static JObject FindRegisterRow(JArray register, string filePath)
        {
            if (register == null || string.IsNullOrWhiteSpace(filePath)) return null;
            string full = Normalise(filePath);
            var rows = register.OfType<JObject>().ToList();
            var byPath = rows.FirstOrDefault(r =>
            {
                string p = r["file_path"]?.ToString() ?? r["file_reference"]?.ToString();
                return !string.IsNullOrWhiteSpace(p) && string.Equals(Normalise(p), full, StringComparison.OrdinalIgnoreCase);
            });
            if (byPath != null) return byPath;
            string name = Path.GetFileName(filePath);
            var byName = rows.Where(r => string.Equals(r["file_name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase)).ToList();
            return byName.Count == 1 ? byName[0] : null;
        }

        /// <summary>The fields the Export Centre flagged as not set on this row (iso_unset).</summary>
        public static List<string> UnsetFields(JObject row)
        {
            if (row?["iso_unset"] is JArray a)
                return a.Select(t => t?.ToString()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            return new List<string>();
        }

        /// <summary>
        /// Decide the upload facts for one file. <paramref name="bundle"/> is the ACC Publish
        /// bundle record ONLY when it names this exact file (null otherwise).
        /// </summary>
        public static AccFileIsoFacts Decide(string filePath, AccBundleRecord bundle, JObject registerRow)
        {
            var f = new AccFileIsoFacts { RegisterRow = registerRow };
            string fileName = Path.GetFileName(filePath ?? string.Empty);
            f.DocumentNumber = Path.GetFileNameWithoutExtension(fileName);

            if (ExportIsoFields.NameCarriesNotSetMarker(fileName))
            {
                f.Refusal = $"'{fileName}' was exported from a sheet with no suitability and/or no revision " +
                            $"(its name carries the '{ExportIsoFields.NotSetSuitability}' / '{ExportIsoFields.NotSetRevision}' " +
                            "not-set marker). Set the sheet's suitability and revision, re-export, then upload.";
                return f;
            }
            var unset = UnsetFields(registerRow);
            if (unset.Count > 0)
            {
                f.Refusal = $"The document register records '{fileName}' as exported with {ExportIsoFields.DescribeUnset(unset)}. " +
                            "Set the sheet's suitability and revision, re-export, then upload.";
                return f;
            }

            RegisterEntry mapped = registerRow != null ? DocumentRegisterMerge.MapRegisterRow(registerRow) : null;
            string regDocNumber = registerRow?["doc_number"]?.ToString()?.Trim();
            if (!string.IsNullOrEmpty(regDocNumber))
            {
                f.DocumentNumber = regDocNumber;
                f.Notes.Add("Document number from the document register.");
            }

            if (bundle != null)
            {
                f.Suitability = bundle.Suitability ?? string.Empty;
                f.Revision = bundle.Revision ?? string.Empty;
                if (f.Revision.Length == 0)
                    f.Notes.Add("No ISO Revision is sent: " +
                                (string.IsNullOrWhiteSpace(bundle.RevisionNote) ? "the bundle record carries none" : bundle.RevisionNote) + ".");
                return f;
            }

            // R14: an S0 the register filled in by convention is not a suitability anyone
            // chose; sending it would file the document as WIP/S0 in ACC unasked.
            bool suitDefaulted = registerRow?["suitability_defaulted"]?.Type == JTokenType.Boolean
                                 && (bool)registerRow["suitability_defaulted"];
            if (suitDefaulted)
                f.Notes.Add("The document register's suitability for this file is the S0 default, not a recorded code, so it is not sent.");
            if (mapped != null)
            {
                if (!suitDefaulted && !ExportIsoFields.IsNotSetMarker(mapped.Suitability) &&
                    !string.IsNullOrEmpty(Iso19650Suitability.CdeStateFor(Iso19650Suitability.ExtractCode(mapped.Suitability))))
                {
                    f.Suitability = Iso19650Suitability.ExtractCode(mapped.Suitability);
                    f.Notes.Add($"Suitability {f.Suitability} from the document register.");
                }
                if (!ExportIsoFields.IsNotSetMarker(mapped.Revision))
                {
                    f.Revision = mapped.Revision.Trim();
                    f.Notes.Add($"Revision {f.Revision} from the document register.");
                }
            }
            if (f.Revision.Length == 0)
                f.Notes.Add(registerRow == null
                    ? "The file is not in the document register, so no ISO Revision is sent."
                    : "The document register records no revision for this file, so no ISO Revision is sent.");
            return f;
        }

        private static string Normalise(string p)
        {
            try { return Path.GetFullPath(p.Trim()); }
            catch (Exception) { return p.Trim(); }
        }
    }
}
