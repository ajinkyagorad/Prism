using UnityEngine;

namespace Prism.Worlds.AlgorithmCity
{
    /// <summary>
    /// One elementary, honestly-counted event of a running sort. Everything the world draws —
    /// the highlighted pair, the bead that physically crosses to a new slot, the mote added to a
    /// cost spire, the point appended to the growth curve — is a direct reaction to one of these,
    /// never a guess at what the algorithm "would" be doing.
    /// </summary>
    public struct SortStep
    {
        public enum StepKind { Compare, Drain, Publish, Done }

        public StepKind Kind;

        /// <summary>Compare: the two live array indices just looked at. Drain: whichever side
        /// supplied the value (the other is -1). Publish/Done: unused (-1).</summary>
        public int A, B;

        /// <summary>Compare only: true when B's value was the smaller / earlier one.</summary>
        public bool Swapped;

        /// <summary>Compare/Drain: the index a value was written to (insertion: the slot after a
        /// swap; merge: the position in the merge buffer).</summary>
        public int WriteIndex;

        /// <summary>Publish only: the half-open window [WindowLo, WindowHi) whose contents are
        /// now finally sorted relative to each other and safe to read as ground truth.</summary>
        public int WindowLo, WindowHi;
    }

    /// <summary>
    /// A sort, mid-flight. <see cref="Step"/> performs exactly one comparison (or the drain/publish
    /// bookkeeping either side of one) and returns immediately — nothing here ever races ahead of
    /// what the world has drawn. <see cref="Values"/> is the live array: it is the single source of
    /// truth, so a learner who reaches in and swaps two entries by hand changes what the very next
    /// Step() call actually compares. That is not a special case anywhere in this file; it falls out
    /// of reading the array fresh every time.
    /// </summary>
    public interface ISortProcess
    {
        string Name { get; }
        int[] Values { get; }
        int Comparisons { get; }
        int Moves { get; }
        bool IsDone { get; }
        SortStep Step();
    }

    /// <summary>
    /// Classic insertion sort: adjacent compare-and-swap, walking a new element left until it is
    /// seated. One comparison per <see cref="Step"/>. Cost is wildly input-sensitive — the whole
    /// reason it stays honest to animate: a nearly-sorted batch visibly costs almost nothing, and a
    /// reversed one visibly costs everything, before either number is ever named.
    /// </summary>
    public class InsertionSortProcess : ISortProcess
    {
        public string Name => "Insertion";
        public int[] Values { get; }
        public int Comparisons { get; private set; }
        public int Moves { get; private set; }
        public bool IsDone { get; private set; }

        int _i = 1, _j;
        bool _jInit;

        public InsertionSortProcess(int[] values)
        {
            Values = values;
            IsDone = values == null || values.Length < 2;
        }

        public SortStep Step()
        {
            if (IsDone) return new SortStep { Kind = SortStep.StepKind.Done, A = -1, B = -1 };

            if (!_jInit) { _j = _i; _jInit = true; }

            if (_j <= 0)
            {
                _i++;
                if (_i >= Values.Length)
                {
                    IsDone = true;
                    return new SortStep { Kind = SortStep.StepKind.Done, A = -1, B = -1 };
                }
                _j = _i;
            }

            int a = _j - 1, b = _j;
            Comparisons++;
            bool swap = Values[a] > Values[b];
            if (swap)
            {
                (Values[a], Values[b]) = (Values[b], Values[a]);
                Moves++;
                _j--;
            }
            else
            {
                _j = 0;   // seated; the next Step() advances to the next outer element
            }

            return new SortStep
            {
                Kind = SortStep.StepKind.Compare,
                A = a, B = b,
                Swapped = swap,
                WriteIndex = swap ? a : -1
            };
        }
    }

    /// <summary>
    /// Bottom-up (iterative) merge sort: passes of width 1, 2, 4, ... merging adjacent runs, no
    /// recursion. Same algorithm family and the same O(n log n) comparison count as the textbook
    /// top-down version — chosen over top-down specifically because a flat, resumable state machine
    /// can be driven one honest comparison at a time and safely resumed after a learner's hands have
    /// changed the array, with no call stack to reconcile.
    ///
    /// A merge window is only trustworthy once <see cref="SortStep.StepKind.Publish"/> fires for it:
    /// mid-merge, <see cref="Values"/> still shows the PRE-merge contents of that window (the buffer
    /// is private), which is why the world only re-reads a window's colours and rank on Publish.
    /// </summary>
    public class MergeSortProcess : ISortProcess
    {
        public string Name => "Merge";
        public int[] Values { get; }
        public int Comparisons { get; private set; }
        public int Moves { get; private set; }
        public bool IsDone { get; private set; }

        /// <summary>How many full passes have completed for index i — the row it rides on in the
        /// cascade. 0 = untouched; rises by one each time a window containing it is published.</summary>
        public int[] RowOf { get; }

        readonly int[] _buf;
        readonly int _n;
        int _width;
        int _lo, _mid, _hi;
        int _i, _j, _k;
        int _pass;
        bool _merging;

        public MergeSortProcess(int[] values)
        {
            Values = values;
            _n = values?.Length ?? 0;
            _buf = new int[_n];
            RowOf = new int[_n];
            _width = 1;
            _lo = 0;
            IsDone = _n < 2;
        }

        /// <summary>
        /// A process pre-seeded to perform exactly the ONE merge that combines two already-sorted
        /// runs — <c>values[0..leftCount)</c> and <c>values[leftCount..)</c> — into one sorted whole.
        /// Used by the Create stage to honestly combine two independently-sorted halves of a
        /// learner-built pipeline: it is the same comparison-counting code as every other merge in
        /// this file, just started already inside its one and only window.
        /// </summary>
        public static MergeSortProcess ForSingleCombine(int[] values, int leftCount)
        {
            var p = new MergeSortProcess(values);
            leftCount = Mathf.Clamp(leftCount, 0, p._n);

            if (leftCount <= 0 || leftCount >= p._n)
            {
                // Nothing to combine — one side is empty, so the array is already the answer.
                p.IsDone = true;
                return p;
            }

            p._lo = 0; p._mid = leftCount; p._hi = p._n;
            p._i = 0; p._j = leftCount; p._k = 0;
            p._width = p._n;      // so that once this window publishes, width >= n and IsDone follows
            p._merging = true;
            return p;
        }

        public SortStep Step()
        {
            if (IsDone) return new SortStep { Kind = SortStep.StepKind.Done, A = -1, B = -1 };

            int guard = 0;
            while (guard++ < 64)
            {
                if (!_merging)
                {
                    if (_lo >= _n) { _width *= 2; _lo = 0; _pass++; }
                    if (_width >= _n)
                    {
                        IsDone = true;
                        return new SortStep { Kind = SortStep.StepKind.Done, A = -1, B = -1 };
                    }

                    _mid = Mathf.Min(_lo + _width, _n);
                    _hi = Mathf.Min(_lo + _width * 2, _n);

                    if (_mid >= _hi)
                    {
                        // An odd run left over at the end of this pass: nothing to merge it with,
                        // but it still rides the cascade up a row so it is not left visibly behind.
                        for (int x = _lo; x < _mid; x++) RowOf[x] = _pass + 1;
                        var carried = new SortStep
                        {
                            Kind = SortStep.StepKind.Publish,
                            A = -1, B = -1,
                            WindowLo = _lo, WindowHi = _mid
                        };
                        _lo += _width * 2;
                        return carried;
                    }

                    _i = _lo; _j = _mid; _k = _lo;
                    _merging = true;
                }

                if (_i < _mid && _j < _hi)
                {
                    Comparisons++;
                    bool leftFirst = Values[_i] <= Values[_j];
                    int from = leftFirst ? _i : _j;
                    _buf[_k] = Values[from];
                    var step = new SortStep
                    {
                        Kind = SortStep.StepKind.Compare,
                        A = _i, B = _j,
                        Swapped = !leftFirst,
                        WriteIndex = _k
                    };
                    if (leftFirst) _i++; else _j++;
                    _k++;
                    return step;
                }

                if (_i < _mid || _j < _hi)
                {
                    bool fromLeft = _i < _mid;
                    int src = fromLeft ? _i : _j;
                    _buf[_k] = Values[src];
                    var step = new SortStep
                    {
                        Kind = SortStep.StepKind.Drain,
                        A = fromLeft ? src : -1,
                        B = fromLeft ? -1 : src,
                        WriteIndex = _k
                    };
                    if (fromLeft) _i++; else _j++;
                    _k++;
                    Moves++;
                    return step;
                }

                // This window is fully merged in the buffer. Publish it back and report the window
                // as one discrete event, so the world can re-colour and re-rank it in one move.
                for (int x = _lo; x < _hi; x++) Values[x] = _buf[x];
                for (int x = _lo; x < _hi; x++) RowOf[x] = _pass + 1;
                int lo = _lo, hi = _hi;
                _lo += _width * 2;
                _merging = false;
                return new SortStep { Kind = SortStep.StepKind.Publish, A = -1, B = -1, WindowLo = lo, WindowHi = hi };
            }

            // Defensive only: a well-formed n never trips the guard. Fail to Done rather than hang.
            IsDone = true;
            return new SortStep { Kind = SortStep.StepKind.Done, A = -1, B = -1 };
        }
    }
}
