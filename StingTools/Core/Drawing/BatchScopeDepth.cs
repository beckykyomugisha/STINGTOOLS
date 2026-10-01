// StingTools — Drawing Template Manager · nesting of production batch scopes
//
// DTW-108: DrawingProducer.PrimeBatchCaches reset every per-batch cache before priming,
// and ResetBatchCaches dropped them. A batch opened inside another (the Setup Wizard run
// from an outer batch, a workflow step inside a produce-and-export run) therefore wiped
// the outer batch's caches on entry and again on exit — including the STACK-1 table of
// which sheet this run already used for which context, so the outer batch could hand the
// same sheet to two contexts.
//
// This counts the nesting. Only the outermost scope primes; only its exit resets.
//
// DTW-142: a nested scope on ANOTHER document must prime (the caches answer one document
// at a time) — but priming used to overwrite the outer batch's caches, STACK-1 claims
// included, and on exit the scope stayed keyed to the inner document. The scopes are now
// a stack of (document, nesting count, the outer caches set aside): entering another
// document captures the outer caches, leaving it resets the inner ones and hands the
// outer ones back to be restored.
//
// Revit-free: StingTools.Tags.Tests compiles this file.

using System;
using System.Collections.Generic;

namespace StingTools.Core.Drawing
{
    public sealed class BatchScopeDepth
    {
        private sealed class Frame
        {
            public string DocKey;
            public int Count;
            public object SavedOuter;
        }

        private readonly Stack<Frame> _frames = new Stack<Frame>();

        /// <summary>Scopes currently open.</summary>
        public int Depth
        {
            get { int n = 0; foreach (var f in _frames) n += f.Count; return n; }
        }

        /// <summary>The document the caches currently answer for; null with no scope open.</summary>
        public string DocKey => _frames.Count == 0 ? null : _frames.Peek().DocKey;

        /// <summary>Open a scope with no way to set the outer caches aside.</summary>
        public bool Enter(string docKey) => Enter(docKey, null);

        /// <summary>
        /// Open a scope on the document identified by <paramref name="docKey"/>. True when the
        /// caller must prime the caches: the outermost scope, or a nested one on another
        /// document — in which case <paramref name="captureOuter"/> is called first and what
        /// it returns is handed back by the matching <see cref="Exit(out object)"/>. False for
        /// a nested scope on the document already primed.
        /// </summary>
        public bool Enter(string docKey, Func<object> captureOuter)
        {
            if (_frames.Count > 0)
            {
                var top = _frames.Peek();
                if (string.Equals(top.DocKey, docKey, StringComparison.OrdinalIgnoreCase))
                {
                    top.Count++;
                    return false;
                }
                _frames.Push(new Frame { DocKey = docKey, Count = 1, SavedOuter = captureOuter?.Invoke() });
                return true;
            }
            _frames.Push(new Frame { DocKey = docKey, Count = 1 });
            return true;
        }

        /// <summary>Close a scope; true when the caller must reset the caches.</summary>
        public bool Exit() => Exit(out _);

        /// <summary>
        /// Close a scope. True when the caller must reset the caches: the outermost scope
        /// ended, a scope on another document ended, or no scope was open (a bare reset still
        /// resets). <paramref name="outerToRestore"/> is non-null when, after resetting, the
        /// caller must restore the outer batch's caches it captured on entry.
        /// </summary>
        public bool Exit(out object outerToRestore)
        {
            outerToRestore = null;
            if (_frames.Count == 0) return true;
            var top = _frames.Peek();
            if (top.Count > 1)
            {
                top.Count--;
                return false;
            }
            _frames.Pop();
            outerToRestore = top.SavedOuter;
            return true;
        }
    }
}
