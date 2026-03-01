using System;
using System.Collections.Generic;
using UnityEngine;

public enum PieceDifficulty
{
    Simple,
    Medium,
    Hard
}

public sealed class PieceDefinition
{
    public string Id { get; }
    public int[,] Matrix { get; }
    public Vector2Int[] Offsets { get; }
    public PieceDifficulty Difficulty { get; }
    public int CellCount => Offsets.Length;

    public PieceDefinition(string id, int[,] matrix, Vector2Int[] offsets, PieceDifficulty difficulty)
    {
        Id = id;
        Matrix = matrix;
        Offsets = offsets;
        Difficulty = difficulty;
    }
}

// Matrix-driven piece catalog for Block Puzzle.
// Matrix rows are defined top -> bottom. Runtime offsets are converted to grid space
// with (0,0) at bottom-left so they match GridManager and placement math.
public static class BlockShapes
{
    public const int MinCells = 1;

    public static readonly PieceDefinition[] Definitions;
    public static readonly Vector2Int[][] All;
    public static readonly Color[] Colors = ColorPalette.Blocks;

    static readonly Dictionary<string, PieceDefinition> _definitionByKey = new Dictionary<string, PieceDefinition>();

    static BlockShapes()
    {
        var defs = new List<PieceDefinition>();

        // Sticks: 1x2, 1x3, 1x4, 1x5 (H + V)
        Add(defs, "stick_1x2_h", M("11"), PieceDifficulty.Simple);
        Add(defs, "stick_1x2_v", M("1", "1"), PieceDifficulty.Simple);
        Add(defs, "stick_1x3_h", M("111"), PieceDifficulty.Medium);
        Add(defs, "stick_1x3_v", M("1", "1", "1"), PieceDifficulty.Medium);
        Add(defs, "stick_1x4_h", M("1111"), PieceDifficulty.Medium);
        Add(defs, "stick_1x4_v", M("1", "1", "1", "1"), PieceDifficulty.Medium);
        Add(defs, "stick_1x5_h", M("11111"), PieceDifficulty.Hard);
        Add(defs, "stick_1x5_v", M("1", "1", "1", "1", "1"), PieceDifficulty.Hard);

        // Squares: 2x2, 3x3
        Add(defs, "square_2x2", M("11", "11"), PieceDifficulty.Simple);
        Add(defs, "square_3x3", M("111", "111", "111"), PieceDifficulty.Hard);

        // L-shape 2x2 (all rotations)
        Add(defs, "l2_r0", M("10", "11"), PieceDifficulty.Medium);
        Add(defs, "l2_r1", M("11", "10"), PieceDifficulty.Medium);
        Add(defs, "l2_r2", M("11", "01"), PieceDifficulty.Medium);
        Add(defs, "l2_r3", M("01", "11"), PieceDifficulty.Medium);

        // Large L 3x3 (all rotations)
        Add(defs, "l3_r0", M("100", "100", "111"), PieceDifficulty.Medium);
        Add(defs, "l3_r1", M("111", "100", "100"), PieceDifficulty.Medium);
        Add(defs, "l3_r2", M("111", "001", "001"), PieceDifficulty.Medium);
        Add(defs, "l3_r3", M("001", "001", "111"), PieceDifficulty.Medium);

        // T & Z (non-rotating)
        Add(defs, "t_std", M("111", "010"), PieceDifficulty.Medium);
        Add(defs, "z_std", M("110", "011"), PieceDifficulty.Medium);

        Definitions = defs.ToArray();
        All = new Vector2Int[Definitions.Length][];
        for (int i = 0; i < Definitions.Length; i++)
        {
            All[i] = Definitions[i].Offsets;
            _definitionByKey[CanonicalKey(Definitions[i].Offsets)] = Definitions[i];
        }

        Debug.Log($"[BlockShapes] Matrix catalog ready: {Definitions.Length} pieces.");
    }

    public static Vector2Int[] GetRandom()
    {
        var src = All[UnityEngine.Random.Range(0, All.Length)];
        var copy = new Vector2Int[src.Length];
        Array.Copy(src, copy, src.Length);
        return copy;
    }

    public static Color GetRandomColor()
        => Colors[UnityEngine.Random.Range(0, Colors.Length)];

    public static PieceDefinition GetDefinitionByOffsets(Vector2Int[] offsets)
    {
        if (offsets == null || offsets.Length == 0) return null;
        _definitionByKey.TryGetValue(CanonicalKey(offsets), out var def);
        return def;
    }

    public static string CanonicalKey(Vector2Int[] shape)
    {
        var parts = new string[shape.Length];
        for (int i = 0; i < shape.Length; i++)
            parts[i] = shape[i].x + "," + shape[i].y;
        Array.Sort(parts, StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    static void Add(List<PieceDefinition> defs, string id, int[,] matrix, PieceDifficulty difficulty)
    {
        var offsets = MatrixToOffsets(matrix);
        defs.Add(new PieceDefinition(id, matrix, offsets, difficulty));
    }

    static int[,] M(params string[] rowsTopToBottom)
    {
        if (rowsTopToBottom == null || rowsTopToBottom.Length == 0)
            throw new ArgumentException("Matrix must have at least one row.");

        int h = rowsTopToBottom.Length;
        int w = rowsTopToBottom[0].Length;
        if (w == 0)
            throw new ArgumentException("Matrix rows cannot be empty.");

        var matrix = new int[h, w];
        for (int r = 0; r < h; r++)
        {
            if (rowsTopToBottom[r].Length != w)
                throw new ArgumentException("All matrix rows must have equal width.");

            for (int c = 0; c < w; c++)
            {
                char ch = rowsTopToBottom[r][c];
                matrix[r, c] = ch == '1' ? 1 : 0;
            }
        }
        return matrix;
    }

    static Vector2Int[] MatrixToOffsets(int[,] matrix)
    {
        int h = matrix.GetLength(0);
        int w = matrix.GetLength(1);
        var result = new List<Vector2Int>(h * w);

        // Row 0 is top in matrix; convert to bottom-left origin used by GridManager.
        for (int r = 0; r < h; r++)
        {
            for (int c = 0; c < w; c++)
            {
                if (matrix[r, c] == 0) continue;
                int y = h - 1 - r;
                result.Add(new Vector2Int(c, y));
            }
        }

        if (result.Count == 0)
            throw new ArgumentException("Piece matrix must contain at least one filled cell.");

        return result.ToArray();
    }
}
