// StingTools — Drawing Template Manager · nesting of production batch scopes
//
// DTW-108: DrawingProducer.PrimeBatchCaches reset every per-batch cache before priming,
// and ResetBatchCaches dropped them. A batch opened inside another (the Setup Wizard run
// from an outer batch, a workflow step inside a produce-and-export run) therefore wiped
// the outer batch's caches on entry and again on exit — including the STACK-1 table of
// which sheet this run already used for which context, so the outer batch could hand the
// same sheet to two contexts.
//
// This counts the nesting. Only the outermost scope primes; only its exit resets. A
// nested scope on another document primes (the caches answer one document at a time,
// and DrawingProducer.CacheMatchesDoc sends the other one to the slow path) but still
// leaves the reset to the outermost exit.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;

namespace StingTools.Core.Drawing
{
    public sealed class BatchScopeDepth
    {
        private int _depth;
        private string _docKey;

        /// <summary>Scopes currently open.</summary>
        public int Depth => _depth;

        /// <summary>
        /// Open a scope on the document identified by <paramref name="docKey"/>. True when the
        /// caller must prime the caches: the outermost scope, or a nested one on another
        /// document. False for a nested scope on the document already primed.
        /// </summary>
        public bool Enter(string docKey)
        {
            bool sameDoc = _depth > 0 && string.Equals(_docKey, docKey, StringComparison.OrdinalIgnoreCase);
            _depth++;
            if (sameDoc) return false;
            _docKey = docKey;
            return true;
        }

        /// <summary>
        /// Close a scope. True when the caller must reset the caches: the outermost scope
        /// ended, or no scope was open (a bare reset still resets).
        /// </summary>
        public bool Exit()
        {
            if (_depth <= 1)
            {
                _depth = 0;
                _docKey = null;
                return true;
            }
            _depth--;
            return false;
        }
    }
}
