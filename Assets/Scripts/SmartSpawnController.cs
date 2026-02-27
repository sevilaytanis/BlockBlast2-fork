using System.Collections.Generic;
using UnityEngine;

// Fit-aware, board-state-driven piece spawner for Block Blast 8×8.
//
// Replaces SpawnController + SolvabilityGuard with one cohesive system.
//
// Algorithm overview:
//   A) Weighted sampling  — 3/4/5-cell ratios by score tier; mercy weights on crowded boards.
//   B) History cooldown   — last-N canonical keys kept; same shape cannot appear twice in a row.
//   C) Candidate scoring  — each candidate is evaluated against the live board:
//       score = clears*ClearBonus − pockets*PocketPenalty + fitBonus
//   D) Weighted top-K     — pick randomly from best TopK (not just argmax → feels natural).
//   E) Batch guarantee    — retry up to RerollLimit times; force-rescue if still stuck.
//   F) Mercy mode         — triggers when board is crowded or player hasn't cleared in N turns.
//
// Integration (see TrayManager changes):
//   1. Call SmartSpawnController.Reset() on game start.
//   2. Call SmartSpawnController.GenerateBatch(3, board, score) on each refill.
//   3. GridManager.PlaceBlockRoutine() calls NotifyLinesCleared(n) automatically.
//
// Board convention (matches GridManager._grid[col, row]):
//   board[col, row] == true  →  cell is occupied
//   shape offsets: Vector2Int(col_offset, row_offset)  — same as BlockShapes.All

public static class SmartSpawnController
{
    // ── Tuning knobs ─────────────────────────────────────────────────────────
    //
    // All public so they can be tweaked at runtime in an Editor debug window
    // or from a DebugPanel MonoBehaviour without recompiling.

    // How many candidate shapes to score per slot position.
    // Higher = better board awareness, slightly more CPU. 30 is fine for 8×8.
    public static int CandidateCount = 30;

    // Pick randomly from the best TopK candidates (weighted by score).
    // 1 = always-best (deterministic, boring); 8 = nearly random.  5 is the sweet spot.
    public static int TopK = 5;

    // Max full retry passes when no batch piece fits anywhere on board.
    // Each retry escalates toward mercy weights.
    public static int RerollLimit = 4;

    // Same-shape cooldown window (# of pieces, not batches).
    // LastN=3 means the same shape won't appear in the last 3 pieces spawned.
    public static int LastN = 3;

    // Score thresholds for the three weight tiers.
    public static int ScoreNormalThreshold = 500;
    public static int ScoreLateThreshold   = 2000;

    // Board occupancy mercy threshold.
    // If free cells < MercyLowCells (board > 78 % full on 8×8), mercy activates.
    public static int MercyLowCells = 14;

    // Line-clear drought threshold.
    // If the player hasn't cleared a line in MercyNoLineTurns full batches, mercy activates.
    // Tracked via NotifyLinesCleared() called from GridManager.
    public static int MercyNoLineTurns = 6;

    // Evaluation weights
    public static float ClearBonus    = 10f;  // per row/column cleared after placement
    public static float PocketPenalty =  5f;  // per isolated empty region smaller than MinCells
    public static float FitBonus      =  2f;  // flat bonus when the piece fits at all

    // ── Spawn weights [3-cell, 4-cell, 5-cell, 6+cell] ───────────────────────
    //
    //                              3-cell  4-cell  5-cell  6+cell
    static readonly float[] _wEarly  = { 0.40f, 0.45f, 0.12f, 0.03f }; // score  <  500
    static readonly float[] _wNormal = { 0.33f, 0.42f, 0.17f, 0.08f }; // score  500 – 1999
    static readonly float[] _wLate   = { 0.25f, 0.38f, 0.25f, 0.12f }; // score ≥ 2000
    static readonly float[] _wMercy  = { 0.65f, 0.30f, 0.05f, 0.00f }; // mercy / crowded board

    // ── Shape pools (partitioned by cell count) ───────────────────────────────

    static Vector2Int[][] _pool3;
    static Vector2Int[][] _pool4;
    static Vector2Int[][] _pool5;
    static Vector2Int[][] _poolLarge; // 6+ cell (2×3, 3×2, 3×3)

    // ── Anti-repeat history ───────────────────────────────────────────────────

    // Both structures kept in sync: queue for expiry order, set for O(1) lookup.
    static readonly Queue<string>   _historyQueue = new Queue<string>();
    static readonly HashSet<string> _historySet   = new HashSet<string>();

    // ── Mercy state ───────────────────────────────────────────────────────────

    // Incremented each time a batch has zero line clears (via NotifyLinesCleared).
    // Reset to 0 whenever at least one line is cleared.
    static int _turnsWithoutClear;

    // ── Static constructor ────────────────────────────────────────────────────

    static SmartSpawnController() => BuildPools();

    static void BuildPools()
    {
        var p3     = new List<Vector2Int[]>();
        var p4     = new List<Vector2Int[]>();
        var p5     = new List<Vector2Int[]>();
        var pLarge = new List<Vector2Int[]>();

        foreach (var shape in BlockShapes.All)
        {
            if (shape.Length < BlockShapes.MinCells) continue;
            switch (shape.Length)
            {
                case 3: p3.Add(shape);     break;
                case 4: p4.Add(shape);     break;
                case 5: p5.Add(shape);     break;
                default: pLarge.Add(shape); break; // 6, 9, etc.
            }
        }

        _pool3     = p3.ToArray();
        _pool4     = p4.ToArray();
        _pool5     = p5.ToArray();
        _poolLarge = pLarge.ToArray();

        Debug.Log($"[SmartSpawn] Pools — 3-cell:{_pool3.Length}  " +
                  $"4-cell:{_pool4.Length}  5-cell:{_pool5.Length}  " +
                  $"6+-cell:{_poolLarge.Length}");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// Call on game start and restart to wipe all persistent state.
    public static void Reset()
    {
        _historyQueue.Clear();
        _historySet.Clear();
        _turnsWithoutClear = 0;
    }

    /// Called by GridManager after each line/column clear event.
    /// Drives the "drought mercy" threshold.
    public static void NotifyLinesCleared(int linesCleared)
    {
        if (linesCleared > 0)
            _turnsWithoutClear = 0;
        else
            _turnsWithoutClear++;
    }

    /// Generates a batch of `count` shaped pieces (usually 3).
    ///
    ///   board  — live board snapshot from GridManager.GetBoardSnapshot()
    ///            board[col, row] == true → occupied
    ///   score  — current GameManager.Score (drives difficulty ramp)
    ///
    /// Guarantees: at least one piece in the returned batch has a valid placement.
    public static Vector2Int[][] GenerateBatch(int count, bool[,] board, int score)
    {
        int free = CountFreeCells(board);
        bool mercy = free < MercyLowCells || _turnsWithoutClear >= MercyNoLineTurns;

        float[] weights = mercy ? _wMercy : PickWeights(score);

        // ── Try up to RerollLimit times to get ≥1 fitting piece ──────────────
        for (int roll = 0; roll < RerollLimit; roll++)
        {
            var batch = GenerateBatchInternal(count, board, weights);

            bool anyFit = false;
            foreach (var shape in batch)
                if (HasAnyPlacement(board, shape)) { anyFit = true; break; }

            if (anyFit)
            {
                if (mercy)
                    Debug.Log($"[SmartSpawn] Mercy active (free={free}, drought={_turnsWithoutClear}). Roll {roll}.");
                return batch;
            }

            // Escalate toward mercy weights on each failed roll
            weights = _wMercy;
            Debug.LogWarning($"[SmartSpawn] Roll {roll+1}: zero fits in batch — escalating weights.");
        }

        // ── Absolute fallback: force-rescue slot 0 ────────────────────────────
        Debug.LogWarning("[SmartSpawn] Fallback rescue — forcing a fitting piece into slot 0.");
        var fallback = GenerateBatchInternal(count, board, _wMercy);
        var rescue   = FindAnyFittingShape(board);
        if (rescue != null) fallback[0] = rescue;
        return fallback;
    }

    // ── Board analysis — public so BlockPiece / SolvabilityGuard can use them ─

    /// Returns true if shape can be placed at origin without leaving the board or overlapping.
    public static bool CanPlace(bool[,] board, Vector2Int[] shape, Vector2Int origin)
    {
        int cols = board.GetLength(0);
        int rows = board.GetLength(1);
        foreach (var o in shape)
        {
            int c = origin.x + o.x;
            int r = origin.y + o.y;
            if (c < 0 || c >= cols || r < 0 || r >= rows) return false;
            if (board[c, r]) return false;
        }
        return true;
    }

    /// Returns true if shape fits anywhere on the board.
    public static bool HasAnyPlacement(bool[,] board, Vector2Int[] shape)
    {
        int cols = board.GetLength(0);
        int rows = board.GetLength(1);
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                if (CanPlace(board, shape, new Vector2Int(c, r)))
                    return true;
        return false;
    }

    /// Returns all (col, row) origins where shape can be placed.
    public static List<Vector2Int> GetAllPlacements(bool[,] board, Vector2Int[] shape)
    {
        int cols = board.GetLength(0);
        int rows = board.GetLength(1);
        var result = new List<Vector2Int>();
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                if (CanPlace(board, shape, new Vector2Int(c, r)))
                    result.Add(new Vector2Int(c, r));
        return result;
    }

    /// Returns a new board copy with shape placed at origin.
    public static bool[,] PlaceShape(bool[,] board, Vector2Int[] shape, Vector2Int origin)
    {
        int cols = board.GetLength(0);
        int rows = board.GetLength(1);
        var copy = new bool[cols, rows];
        System.Array.Copy(board, copy, board.Length);
        foreach (var o in shape)
            copy[origin.x + o.x, origin.y + o.y] = true;
        return copy;
    }

    /// Counts full rows + full columns on the board.
    public static int ComputeClears(bool[,] board)
    {
        int cols = board.GetLength(0);
        int rows = board.GetLength(1);
        int clears = 0;

        for (int r = 0; r < rows; r++)
        {
            bool full = true;
            for (int c = 0; c < cols; c++) if (!board[c, r]) { full = false; break; }
            if (full) clears++;
        }
        for (int c = 0; c < cols; c++)
        {
            bool full = true;
            for (int r = 0; r < rows; r++) if (!board[c, r]) { full = false; break; }
            if (full) clears++;
        }

        return clears;
    }

    /// Scores a specific placement.
    ///   + ClearBonus  per line/column that would be cleared
    ///   - PocketPenalty per isolated empty region smaller than MinCells (dead zones)
    ///   + FitBonus    flat bonus whenever the piece actually fits
    public static float EvaluatePlacement(bool[,] board, Vector2Int[] shape, Vector2Int origin)
    {
        var after  = PlaceShape(board, shape, origin);
        int clears = ComputeClears(after);
        var cleared = SimulateClears(after);   // board state after lines removed

        int pocketsBefore = CountSmallPockets(board);
        int pocketsAfter  = CountSmallPockets(cleared);
        int pocketDelta   = pocketsAfter - pocketsBefore;

        return FitBonus
             + clears      * ClearBonus
             - pocketDelta * PocketPenalty;
    }

    // ── Internal batch generation ─────────────────────────────────────────────

    static Vector2Int[][] GenerateBatchInternal(int count, bool[,] board, float[] weights)
    {
        var batch     = new Vector2Int[count][];
        var batchKeys = new HashSet<string>();   // no duplicate within the same batch

        for (int i = 0; i < count; i++)
        {
            // 1. Sample CandidateCount shapes respecting history + batch uniqueness
            var candidates = DrawCandidates(CandidateCount, weights, batchKeys);

            // 2. Score each candidate against the current board
            var scored = ScoreCandidates(candidates, board);

            // 3. Weighted-random pick from top-K
            var chosen = WeightedPickTopK(scored, TopK);

            batch[i] = chosen;

            string key = CanonicalKey(chosen);
            batchKeys.Add(key);
            TrackHistory(chosen);
        }

        return batch;
    }

    // ── Candidate drawing ─────────────────────────────────────────────────────

    /// Samples up to `count` unique shapes (not in history, not in batchKeys).
    /// Falls back to ignoring history if the pool is too small after filtering.
    static List<Vector2Int[]> DrawCandidates(int count, float[] weights,
                                              HashSet<string> batchKeys)
    {
        var candidates = new List<Vector2Int[]>(count);
        var usedKeys   = new HashSet<string>(batchKeys);

        // Pass 1: respect history cooldown
        int maxAttempts = count * 12;
        for (int attempt = 0; attempt < maxAttempts && candidates.Count < count; attempt++)
        {
            var    shape = SampleWeighted(weights);
            string key   = CanonicalKey(shape);
            if (_historySet.Contains(key)) continue;
            if (usedKeys.Contains(key))    continue;
            candidates.Add(shape);
            usedKeys.Add(key);
        }

        // Pass 2: ignore history if still short (small pools or heavily used shapes)
        if (candidates.Count < count)
        {
            var all = new List<Vector2Int[]>(_pool3.Length + _pool4.Length + _pool5.Length + _poolLarge.Length);
            all.AddRange(_pool3); all.AddRange(_pool4); all.AddRange(_pool5); all.AddRange(_poolLarge);
            Shuffle(all);
            foreach (var shape in all)
            {
                if (candidates.Count >= count) break;
                string key = CanonicalKey(shape);
                if (usedKeys.Contains(key)) continue;
                candidates.Add(shape);
                usedKeys.Add(key);
            }
        }

        return candidates;
    }

    // ── Scoring & selection ───────────────────────────────────────────────────

    struct ScoredShape
    {
        public Vector2Int[] Shape;
        public float        Score;
        public bool         Fits;    // has at least one valid placement
    }

    static List<ScoredShape> ScoreCandidates(List<Vector2Int[]> candidates, bool[,] board)
    {
        var result = new List<ScoredShape>(candidates.Count);

        foreach (var shape in candidates)
        {
            var placements = GetAllPlacements(board, shape);

            if (placements.Count == 0)
            {
                // Shape doesn't fit — give it a strongly negative score so it
                // lands below fitting pieces in the top-K selection.
                result.Add(new ScoredShape { Shape = shape, Score = -100f, Fits = false });
                continue;
            }

            // Score the best placement this shape can achieve
            float best = float.MinValue;
            foreach (var origin in placements)
            {
                float s = EvaluatePlacement(board, shape, origin);
                if (s > best) best = s;
            }

            result.Add(new ScoredShape { Shape = shape, Score = best, Fits = true });
        }

        result.Sort((a, b) => b.Score.CompareTo(a.Score));
        return result;
    }

    /// Weighted random pick from the best TopK candidates.
    /// Score² weighting: shapes with higher scores are disproportionately preferred,
    /// but the element of chance prevents fully deterministic (boring) output.
    static Vector2Int[] WeightedPickTopK(List<ScoredShape> scored, int k)
    {
        int take = Mathf.Min(k, scored.Count);
        if (take == 0) return BlockShapes.GetRandom();

        // Shift scores so minimum is 0, add epsilon to keep non-fitting shapes alive
        float minScore = scored[take - 1].Score;
        float totalW   = 0f;
        float[] weights = new float[take];
        for (int i = 0; i < take; i++)
        {
            float shifted = scored[i].Score - minScore + 1f;
            weights[i] = shifted * shifted;   // square → sharpens preference
            totalW    += weights[i];
        }

        float r   = Random.value * totalW;
        float cum = 0f;
        for (int i = 0; i < take; i++)
        {
            cum += weights[i];
            if (r <= cum) return scored[i].Shape;
        }
        return scored[0].Shape;
    }

    // ── History management ────────────────────────────────────────────────────

    static void TrackHistory(Vector2Int[] shape)
    {
        string key = CanonicalKey(shape);
        _historyQueue.Enqueue(key);
        _historySet.Add(key);

        while (_historyQueue.Count > LastN)
        {
            var old = _historyQueue.Dequeue();
            // Only remove from set if this key no longer appears in the queue
            // (the same key could have been added twice if LastN is very short)
            if (!_historyQueue.Contains(old))
                _historySet.Remove(old);
        }
    }

    // ── Board utility ─────────────────────────────────────────────────────────

    /// Returns the board with all full rows and columns cleared.
    static bool[,] SimulateClears(bool[,] board)
    {
        int cols = board.GetLength(0);
        int rows = board.GetLength(1);
        var result = new bool[cols, rows];
        System.Array.Copy(board, result, board.Length);

        var clearRows = new HashSet<int>();
        var clearCols = new HashSet<int>();

        for (int r = 0; r < rows; r++)
        {
            bool full = true;
            for (int c = 0; c < cols; c++) if (!result[c, r]) { full = false; break; }
            if (full) clearRows.Add(r);
        }
        for (int c = 0; c < cols; c++)
        {
            bool full = true;
            for (int r = 0; r < rows; r++) if (!result[c, r]) { full = false; break; }
            if (full) clearCols.Add(c);
        }

        foreach (int r in clearRows) for (int c = 0; c < cols; c++) result[c, r] = false;
        foreach (int c in clearCols) for (int r = 0; r < rows; r++) result[c, r] = false;

        return result;
    }

    /// Counts isolated empty connected-regions that are smaller than MinCells.
    /// These are "dead zones" — no piece can ever fill them, wasting board space.
    static int CountSmallPockets(bool[,] board)
    {
        int cols    = board.GetLength(0);
        int rows    = board.GetLength(1);
        var visited = new bool[cols, rows];
        int pockets = 0;
        var queue   = new Queue<Vector2Int>();

        for (int c = 0; c < cols; c++)
        {
            for (int r = 0; r < rows; r++)
            {
                if (board[c, r] || visited[c, r]) continue;

                // BFS — find the connected empty region
                queue.Clear();
                queue.Enqueue(new Vector2Int(c, r));
                visited[c, r] = true;
                int size = 0;

                while (queue.Count > 0)
                {
                    var pos = queue.Dequeue();
                    size++;
                    foreach (var n in Neighbors4(pos, cols, rows))
                    {
                        if (!board[n.x, n.y] && !visited[n.x, n.y])
                        {
                            visited[n.x, n.y] = true;
                            queue.Enqueue(n);
                        }
                    }
                }

                if (size < BlockShapes.MinCells)
                    pockets++;
            }
        }

        return pockets;
    }

    static int CountFreeCells(bool[,] board)
    {
        int cols  = board.GetLength(0);
        int rows  = board.GetLength(1);
        int count = 0;
        for (int c = 0; c < cols; c++)
            for (int r = 0; r < rows; r++)
                if (!board[c, r]) count++;
        return count;
    }

    // Tries smallest pools first — 3-cell most likely to rescue a crowded board.
    static Vector2Int[] FindAnyFittingShape(bool[,] board)
    {
        var pools = new[] { _pool3, _pool4, _pool5, _poolLarge };
        foreach (var pool in pools)
        {
            var list = new List<Vector2Int[]>(pool);
            Shuffle(list);
            foreach (var shape in list)
                if (HasAnyPlacement(board, shape))
                    return shape;
        }
        return null;   // board is completely full → legitimate game over
    }

    static IEnumerable<Vector2Int> Neighbors4(Vector2Int pos, int cols, int rows)
    {
        if (pos.x > 0)        yield return new Vector2Int(pos.x - 1, pos.y);
        if (pos.x < cols - 1) yield return new Vector2Int(pos.x + 1, pos.y);
        if (pos.y > 0)        yield return new Vector2Int(pos.x, pos.y - 1);
        if (pos.y < rows - 1) yield return new Vector2Int(pos.x, pos.y + 1);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static float[] PickWeights(int score)
    {
        if (score < ScoreNormalThreshold) return _wEarly;
        if (score < ScoreLateThreshold)   return _wNormal;
        return _wLate;
    }

    static Vector2Int[] SampleWeighted(float[] w)
    {
        // w = [3-cell, 4-cell, 5-cell, 6+-cell]
        float total = w[0] + w[1] + w[2] + (w.Length > 3 ? w[3] : 0f);
        float r     = Random.value * total;

        float cum0 = w[0];
        float cum1 = cum0 + w[1];
        float cum2 = cum1 + w[2];

        Vector2Int[][] pool;
        if      (r < cum0) pool = _pool3;
        else if (r < cum1) pool = _pool4;
        else if (r < cum2) pool = _pool5;
        else               pool = _poolLarge;

        // Fallback: cascade to a non-empty pool if selected pool is empty.
        if (pool.Length == 0) pool = _pool4.Length > 0 ? _pool4 : _pool3;
        if (pool.Length == 0) pool = _pool5;
        if (pool.Length == 0) pool = _poolLarge;

        return pool[Random.Range(0, pool.Length)];
    }

    // Sorted coordinate string — order-independent canonical key for duplicate detection.
    // Identical to BlockShapes.CanonicalKey (kept local to avoid cross-class dependency).
    static string CanonicalKey(Vector2Int[] shape)
    {
        var parts = new string[shape.Length];
        for (int i = 0; i < shape.Length; i++)
            parts[i] = shape[i].x + "," + shape[i].y;
        System.Array.Sort(parts, System.StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j   = Random.Range(0, i + 1);
            T   tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        }
    }
}
