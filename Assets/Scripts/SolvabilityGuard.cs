using System.Collections.Generic;
using UnityEngine;

// Post-processes a generated piece batch to prevent total deadlock.
//
// WHEN does the guard fire?
//   Only when ZERO pieces in the batch have a valid board placement AND
//   the cooldown has expired.  It does NOT fire when 1–2 pieces are hard
//   to place — that is intentional challenge.
//
// WHAT does it do?
//   Replaces the largest unplaceable piece with the smallest shape that
//   actually fits on the current board (tries 3-cell first, then 4-cell).
//   Up to MaxRerolls replacements per activation (default 1).
//
// WHY doesn't it feel like cheating?
//   • It only fires on complete deadlocks, which are rare.
//   • The CooldownBatches gap prevents help appearing on back-to-back batches.
//   • A random 3-cell piece reads as "I got a lucky small piece" — not "the
//     game saved me."  The player never sees the guard logic.
//
// Integration:
//   1. Call SolvabilityGuard.Reset() on game start / restart.
//   2. After SpawnController.GenerateBatch(), call ApplyGuard(batch, score).
//   3. Then spawn GameObjects from the (possibly modified) batch.
//
// Tuning after playtests:
//   • Players still hit game over too fast → lower CooldownBatches (2 → 1)
//     or raise MaxRerolls to 2.
//   • Guard fires too often, game feels rigged → raise CooldownBatches (3 → 5).
//   • Late-game too easy → guard is already score-agnostic; to disable it at
//     high scores add "if (score > 5000) return false;" at the top of ApplyGuard.
public static class SolvabilityGuard
{
    // ── Tuning knobs (adjust post-playtest) ───────────────────────────────────

    // Min pieces per batch that must have at least one valid board placement.
    // 1 = "at least one piece must always be placeable."
    // Raise to 2 for a more forgiving game; set 0 to disable the guard entirely.
    public static int MinFitCount = 1;

    // Number of batches that must pass between guard activations.
    // 3 means the guard can help at most once every 4 refills.
    // Lower = more generous; higher = harder / less visible.
    public static int CooldownBatches = 3;

    // Max piece replacements per single guard activation.
    // 1 is the right default: replace only the worst piece, leave the rest as challenge.
    public static int MaxRerolls = 1;

    // ── Telemetry (read in Editor / analytics; not shown to player) ───────────

    public static int TriggerCount { get; private set; }  // times guard has fired
    public static int TotalBatches { get; private set; }  // batches evaluated

    // ── Internal state ────────────────────────────────────────────────────────

    // Counts batches since the last guard activation.
    // Starts > CooldownBatches so the guard is immediately available at game start.
    static int _cooldown = 999;

    // ── Public API ────────────────────────────────────────────────────────────

    // Reset state between game sessions (static fields persist across scene reloads).
    public static void Reset()
    {
        _cooldown = CooldownBatches + 1;   // ready from the first batch
        // Intentionally keep TriggerCount / TotalBatches — they're per-session stats
        // useful for tuning; reset them only if you need per-run analytics.
    }

    // Post-process a freshly generated batch of piece shapes.
    //   batch[] — array of Vector2Int[] shapes (modified in-place if guard fires)
    //   score   — current player score (reserved for future score-gated tuning)
    // Returns true if any piece was replaced.
    public static bool ApplyGuard(Vector2Int[][] batch, int score)
    {
        if (MinFitCount == 0 || GridManager.Instance == null) return false;

        TotalBatches++;

        // Still in cooldown — increment counter and skip.
        if (_cooldown <= CooldownBatches)
        {
            _cooldown++;
            return false;
        }

        // Count how many pieces in the batch can be placed anywhere on the board.
        int fitCount = CountFitting(batch);
        if (fitCount >= MinFitCount) return false;   // enough pieces fit; no help needed

        // ── Guard fires ───────────────────────────────────────────────────────

        bool replaced = false;

        for (int repl = 0; repl < MaxRerolls; repl++)
        {
            // Find the largest (hardest) piece that cannot be placed.
            int worstIdx = WorstUnfitIndex(batch);
            if (worstIdx < 0) break;   // all pieces now fit (shouldn't happen on first pass)

            // Find the smallest shape that actually fits on the current board.
            var rescue = FindFittingShape();
            if (rescue == null) break;   // board is nearly full — legitimate game over ahead

            Debug.Log($"[SolvabilityGuard] Rescue! batch #{TotalBatches}, trigger #{TriggerCount + 1}. " +
                      $"Slot {worstIdx}: {batch[worstIdx].Length}-cell → {rescue.Length}-cell. " +
                      $"fitCount before: {fitCount}.");

            batch[worstIdx] = rescue;
            replaced = true;

            // Stop early if we've already met the MinFitCount goal.
            if (CountFitting(batch) >= MinFitCount) break;
        }

        if (replaced)
        {
            TriggerCount++;
            _cooldown = 0;   // start cooldown
        }

        return replaced;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    static int CountFitting(Vector2Int[][] batch)
    {
        int n = 0;
        foreach (var shape in batch)
            if (shape != null && GridManager.Instance.CanPlaceAnywhere(shape))
                n++;
        return n;
    }

    // Returns the index of the largest piece that cannot be placed anywhere.
    // Largest is the most frustrating to receive when stuck, so replace it first.
    static int WorstUnfitIndex(Vector2Int[][] batch)
    {
        int worst = -1, worstSize = -1;
        for (int i = 0; i < batch.Length; i++)
        {
            if (batch[i] == null) continue;
            if (!GridManager.Instance.CanPlaceAnywhere(batch[i]) &&
                batch[i].Length > worstSize)
            {
                worstSize = batch[i].Length;
                worst     = i;
            }
        }
        return worst;
    }

    // Returns a random shape from BlockShapes.All that fits on the current board.
    // Tries 3-cell shapes first (highest placement probability on a crowded board),
    // then 4-cell, then 5-cell.  Within each size group the order is shuffled so
    // the rescue piece doesn't always look identical to the player.
    static Vector2Int[] FindFittingShape()
    {
        var by3 = new List<Vector2Int[]>();
        var by4 = new List<Vector2Int[]>();
        var by5 = new List<Vector2Int[]>();

        foreach (var shape in BlockShapes.All)
        {
            if (shape.Length < BlockShapes.MinCells) continue;
            switch (shape.Length)
            {
                case 3:  by3.Add(shape); break;
                case 4:  by4.Add(shape); break;
                default: by5.Add(shape); break;
            }
        }

        // Try each size group smallest-first, shuffled within the group.
        foreach (var pool in new[] { by3, by4, by5 })
        {
            Shuffle(pool);
            foreach (var shape in pool)
                if (GridManager.Instance.CanPlaceAnywhere(shape))
                    return shape;
        }

        return null;   // every shape is blocked → board is completely full
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
