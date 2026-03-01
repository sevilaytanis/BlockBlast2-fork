using System.Collections.Generic;
using UnityEngine;

// Difficulty-driven, matrix-shape-aware spawner.
// Guarantees at least one generated piece can fit the current board.
public static class SmartSpawnController
{
    public struct DifficultyWeights
    {
        public float Simple;
        public float Medium;
        public float Hard;

        public DifficultyWeights(float simple, float medium, float hard)
        {
            Simple = simple;
            Medium = medium;
            Hard = hard;
        }
    }

    // DDA thresholds
    public static int ScoreHardRampThreshold = 1000;

    // score < 1000 -> mostly easy blocks
    public static DifficultyWeights EarlyWeights = new DifficultyWeights(
        simple: 0.72f,
        medium: 0.23f,
        hard: 0.05f
    );

    // score >= 1000 -> more hard blocks (1x5, 3x3)
    public static DifficultyWeights LateWeights = new DifficultyWeights(
        simple: 0.42f,
        medium: 0.33f,
        hard: 0.25f
    );

    static readonly List<PieceDefinition> _simple = new List<PieceDefinition>();
    static readonly List<PieceDefinition> _medium = new List<PieceDefinition>();
    static readonly List<PieceDefinition> _hard = new List<PieceDefinition>();

    static SmartSpawnController() => RebuildPools();

    public static void Reset()
    {
        // Reserved for future stateful spawn logic (streaks, pity timer, etc.)
    }

    public static void NotifyLinesCleared(int linesCleared)
    {
        // Reserved for future adaptive tuning hooks.
    }

    public static Vector2Int[][] GenerateBatch(int count, bool[,] board, int score)
    {
        if (count <= 0) return new Vector2Int[0][];
        if (board == null) board = new bool[8, 8];

        var weights = PickWeights(score);
        var batch = new Vector2Int[count][];
        var usedIds = new HashSet<string>();

        for (int i = 0; i < count; i++)
        {
            var picked = PickWeightedDefinition(weights, usedIds);
            batch[i] = picked.Offsets;
            usedIds.Add(picked.Id);
        }

        EnsureAtLeastOneFits(batch, board, score);
        return batch;
    }

    static DifficultyWeights PickWeights(int score)
    {
        return score < ScoreHardRampThreshold ? EarlyWeights : LateWeights;
    }

    static void RebuildPools()
    {
        _simple.Clear();
        _medium.Clear();
        _hard.Clear();

        foreach (var def in BlockShapes.Definitions)
        {
            switch (def.Difficulty)
            {
                case PieceDifficulty.Simple: _simple.Add(def); break;
                case PieceDifficulty.Medium: _medium.Add(def); break;
                case PieceDifficulty.Hard: _hard.Add(def); break;
            }
        }
    }

    static PieceDefinition PickWeightedDefinition(DifficultyWeights weights, HashSet<string> usedIds)
    {
        // Try a few times while avoiding duplicates in same tray batch.
        for (int i = 0; i < 20; i++)
        {
            var list = PickDifficultyPool(weights);
            if (list.Count == 0) continue;
            var candidate = list[Random.Range(0, list.Count)];
            if (!usedIds.Contains(candidate.Id)) return candidate;
        }

        // Fallback: any non-used piece
        foreach (var def in BlockShapes.Definitions)
            if (!usedIds.Contains(def.Id))
                return def;

        // Last fallback (count > catalog size)
        return BlockShapes.Definitions[Random.Range(0, BlockShapes.Definitions.Length)];
    }

    static List<PieceDefinition> PickDifficultyPool(DifficultyWeights weights)
    {
        float total = Mathf.Max(0.0001f, weights.Simple + weights.Medium + weights.Hard);
        float r = Random.value * total;

        if (r < weights.Simple) return _simple;
        if (r < weights.Simple + weights.Medium) return _medium;
        return _hard;
    }

    static void EnsureAtLeastOneFits(Vector2Int[][] batch, bool[,] board, int score)
    {
        for (int i = 0; i < batch.Length; i++)
            if (batch[i] != null && HasAnyPlacement(board, batch[i]))
                return;

        var rescue = FindAnyFittingShape(board, score);
        if (rescue == null) return; // legitimate game-over board (nothing fits)

        int slot = Random.Range(0, batch.Length);
        batch[slot] = rescue.Offsets;
    }

    static PieceDefinition FindAnyFittingShape(bool[,] board, int score)
    {
        // Prefer easier rescue pieces to avoid impossible-feeling trays.
        var orderedPools = new List<List<PieceDefinition>> { _simple, _medium, _hard };
        if (score >= ScoreHardRampThreshold)
            orderedPools = new List<List<PieceDefinition>> { _medium, _simple, _hard };

        foreach (var pool in orderedPools)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                var def = pool[i];
                if (HasAnyPlacement(board, def.Offsets))
                    return def;
            }
        }

        return null;
    }

    // ---- Grid compatibility helpers (shared API) ----

    public static bool CanPlace(bool[,] board, Vector2Int[] shape, Vector2Int origin)
    {
        int cols = board.GetLength(0);
        int rows = board.GetLength(1);
        for (int i = 0; i < shape.Length; i++)
        {
            int c = origin.x + shape[i].x;
            int r = origin.y + shape[i].y;
            if (c < 0 || c >= cols || r < 0 || r >= rows) return false;
            if (board[c, r]) return false;
        }
        return true;
    }

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

    public static bool[,] PlaceShape(bool[,] board, Vector2Int[] shape, Vector2Int origin)
    {
        int cols = board.GetLength(0);
        int rows = board.GetLength(1);
        var copy = new bool[cols, rows];
        System.Array.Copy(board, copy, board.Length);
        for (int i = 0; i < shape.Length; i++)
            copy[origin.x + shape[i].x, origin.y + shape[i].y] = true;
        return copy;
    }

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
}
