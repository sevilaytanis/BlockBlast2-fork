using System.Collections.Generic;
using UnityEngine;

// Drives weighted, anti-repeat piece selection for the 3-slot tray.
//
// Design goals (casual Block Blast retention):
//   • 3-cell pieces act as a safety valve — rescues near-full boards, prevents dead-ends.
//   • 4-cell pieces are the core loop — familiar tetromino feel, high line-clear potential.
//   • 5-cell pieces provide excitement spikes — used sparingly to avoid board blocking.
//   • No duplicate shape within the same 3-piece batch (player sees all 3 simultaneously).
//   • Same shape cannot lead two consecutive batches (subtly noticeable and annoying).
//   • Same cell-count capped at kMaxSameSize consecutive pieces before forcing a switch.
//   • Gentle score-based weight shift: more 5-cell pieces as the player improves.
//
// Integration:
//   Call  SpawnController.BeginBatch()  once before each set of 3 spawns.
//   Call  SpawnController.Next(score)   once per slot inside that batch.
//   Call  SpawnController.Reset()       on game restart (static state persists across scenes).
public static class SpawnController
{
    // ── Spawn weights [0]=3-cell  [1]=4-cell  [2]=5-cell ─────────────────────
    //
    // Tuning targets — balanced 3-cell/4-cell split; 5-cell used sparingly early
    // and more aggressively at high scores.
    // Adjust after playtests: increase _weightsEarly[0] if players hit dead-ends
    // early; decrease _weightsLate[2] if late-game feels punishing.
    //
    //                        3-cell  4-cell  5-cell
    static readonly float[] _weightsEarly  = { 0.40f, 0.45f, 0.15f }; // score <  500
    static readonly float[] _weightsNormal = { 0.35f, 0.45f, 0.20f }; // score  500–1999
    static readonly float[] _weightsLate   = { 0.30f, 0.40f, 0.30f }; // score ≥ 2000

    // Max consecutive pieces of the same cell-count before forcing a switch.
    // 3 means "three 4-cell pieces in a row is fine, four is not."
    // Raise to 4-5 if you want looser variety; lower to 2 for tighter balance.
    const int kMaxSameSize = 3;

    // Max retry attempts per Next() call before giving up and falling back.
    // 30 is overkill in normal play (typical settle in ≤ 3 attempts).
    const int kMaxAttempts = 30;

    // ── Shape pools partitioned by cell count ─────────────────────────────────

    static Vector2Int[][] _pool3;
    static Vector2Int[][] _pool4;
    static Vector2Int[][] _pool5;

    // ── Anti-repeat state ─────────────────────────────────────────────────────

    // Canonical keys of shapes already accepted in the current batch.
    // Cleared by BeginBatch() at the start of each new set of 3.
    static readonly HashSet<string> _batchKeys = new HashSet<string>();

    // Key of the last accepted shape (across batches).
    // Prevents the same piece from *leading* the very next batch.
    static string _prevKey;

    // Consecutive-same-size tracking.
    static int _sameSize;
    static int _lastSize = -1;

    // ── Static constructor ────────────────────────────────────────────────────

    static SpawnController()
    {
        BuildPools();
    }

    static void BuildPools()
    {
        var p3 = new List<Vector2Int[]>();
        var p4 = new List<Vector2Int[]>();
        var p5 = new List<Vector2Int[]>();

        foreach (var shape in BlockShapes.All)
        {
            if (shape.Length < BlockShapes.MinCells) continue;
            switch (shape.Length)
            {
                case 3:  p3.Add(shape); break;
                case 4:  p4.Add(shape); break;
                default: p5.Add(shape); break;
            }
        }

        _pool3 = p3.ToArray();
        _pool4 = p4.ToArray();
        _pool5 = p5.ToArray();

        Debug.Log($"[SpawnController] Pools ready — " +
                  $"3-cell:{_pool3.Length}  4-cell:{_pool4.Length}  5-cell:{_pool5.Length}");
    }

    // ── Public API ────────────────────────────────────────────────────────────

    // Call once BEFORE spawning each new set of 3 pieces.
    // Clears the within-batch duplicate guard.
    public static void BeginBatch()
    {
        _batchKeys.Clear();
    }

    // Returns the next shape for one tray slot.
    //   score — current player score (drives the weight ramp)
    public static Vector2Int[] Next(int score)
    {
        float[] weights = PickWeights(score);

        for (int attempt = 0; attempt < kMaxAttempts; attempt++)
        {
            // ── 1. Choose size group ──────────────────────────────────────────
            int size = SampleSize(weights);

            // Anti-repeat: if consecutive same-size limit reached, override.
            if (size == _lastSize && _sameSize >= kMaxSameSize)
                size = ForceDifferentSize(size);

            // ── 2. Choose shape from that pool ────────────────────────────────
            var pool = PoolFor(size);
            if (pool.Length == 0) continue;   // shouldn't happen with valid data

            var    shape = pool[Random.Range(0, pool.Length)];
            string key   = CanonicalKey(shape);

            // Anti-repeat: no duplicate within this batch.
            if (_batchKeys.Contains(key)) continue;

            // Anti-repeat: don't let the same shape lead the fresh batch
            // (only enforce for the first half of retries to avoid soft-lock).
            if (attempt < kMaxAttempts / 2 && key == _prevKey) continue;

            // ── 3. Accept ─────────────────────────────────────────────────────
            _batchKeys.Add(key);
            _prevKey = key;

            if (size == _lastSize) _sameSize++;
            else { _lastSize = size; _sameSize = 1; }

            return shape;
        }

        // Absolute fallback — reachable only if all 3 pool-slots are already in the
        // batch (impossible with 6/19/8 pool sizes) or extreme edge cases.
        Debug.LogWarning("[SpawnController] Retry budget exhausted; falling back to GetRandom().");
        return BlockShapes.GetRandom();
    }

    // Generates a complete batch of `count` pieces in one call.
    // Equivalent to BeginBatch() followed by count × Next(score).
    // Returns shapes[] ready for SolvabilityGuard.ApplyGuard().
    public static Vector2Int[][] GenerateBatch(int count, int score)
    {
        BeginBatch();
        var batch = new Vector2Int[count][];
        for (int i = 0; i < count; i++)
            batch[i] = Next(score);
        return batch;
    }

    // Reset anti-repeat state between game sessions.
    // Call from TrayManager.Start() or GameManager.RestartGame().
    public static void Reset()
    {
        _batchKeys.Clear();
        _prevKey  = null;
        _sameSize = 0;
        _lastSize = -1;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    static float[] PickWeights(int score)
    {
        if (score <  500) return _weightsEarly;
        if (score < 2000) return _weightsNormal;
        return _weightsLate;
    }

    // Weighted sample over three size groups.
    static int SampleSize(float[] w)
    {
        float total = w[0] + w[1] + w[2];
        float r     = Random.value * total;
        if (r < w[0])          return 3;
        if (r < w[0] + w[1])   return 4;
        return 5;
    }

    // When same-size cap is hit, return a different size.
    // Biases toward 4-cell as the neutral default.
    static int ForceDifferentSize(int blocked)
    {
        if (blocked == 4) return Random.value < 0.5f ? 3 : 5;
        return 4;
    }

    static Vector2Int[][] PoolFor(int size)
    {
        if (size == 3) return _pool3;
        if (size == 5) return _pool5;
        return _pool4;
    }

    // Sorted coordinate string — identical algorithm to BlockShapes.CanonicalKey.
    static string CanonicalKey(Vector2Int[] shape)
    {
        var parts = new string[shape.Length];
        for (int i = 0; i < shape.Length; i++)
            parts[i] = shape[i].x + "," + shape[i].y;
        System.Array.Sort(parts, System.StringComparer.Ordinal);
        return string.Join("|", parts);
    }
}
