using System.Collections.Generic;
using UnityEngine;

// Pure static data - no MonoBehaviour needed.
// All shapes use (col, row) offsets with (0,0) at the bottom-left of each bounding box.
// ASCII diagrams show the shape as it appears on screen (row 0 at bottom, █ = filled, · = empty).
//
// Pool design rationale (casual Block Blast retention):
//   3-cell (~35-40 %) — easy to fit anywhere; rewards quick decisions; prevents early dead-ends
//   4-cell (~40-45 %) — bread-and-butter; familiar from Tetris family; good line-clear density
//   5-cell (~15-30 %) — exciting, high-score moments; occasional challenge spike; not so large
//                       that placement becomes frustrating on an 8×8 grid
//   See SpawnController for exact per-score-tier weights.
// All rotations/mirrors of each canonical shape are separate entries because the player
// sees the piece as-is and cannot rotate during gameplay.
public static class BlockShapes
{
    // ── Constraint ────────────────────────────────────────────────────────────
    public const int MinCells = 3;  // no 1-cell or 2-cell pieces ever spawn

    // ── Shape catalogue (30 shapes) ───────────────────────────────────────────
    public static readonly Vector2Int[][] All = new Vector2Int[][]
    {
        // ════════════════════════════════════════════════════════════════════
        //  3-CELL SHAPES  (6 shapes)
        //  Easy to place, high board survival, good for new players.
        // ════════════════════════════════════════════════════════════════════

        // I3 — straight trominoes
        // ███
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0) },            // I3H

        // █
        // █
        // █
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(0,2) },            // I3V

        // Corner-3 — all 4 rotations of the 3-cell L/corner shape
        // ·█   row1
        // ██   row0
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1) },            // C3_A

        // █·   row1
        // ██   row0
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(1,1) },            // C3_B

        // ██   row1
        // █·   row0
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1) },            // C3_C

        // ██   row1
        // ·█   row0
        new[] { new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1) },            // C3_D

        // ════════════════════════════════════════════════════════════════════
        //  4-CELL SHAPES  (19 shapes)
        //  Core gameplay loop.  All tetrominoes + all their rotations.
        // ════════════════════════════════════════════════════════════════════

        // I4 — long bars (very satisfying line-clear potential)
        // ████
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(3,0) },  // I4H

        // █
        // █
        // █
        // █
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(0,3) },  // I4V

        // O4 — 2×2 square (always fits in any 2×2 opening)
        // ██
        // ██
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1) },  // O4

        // T4 — all 4 rotations
        // ·█·
        // ███
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(1,1) },  // T4_up

        // ███
        // ·█·
        new[] { new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(2,1) },  // T4_down

        // ·█
        // ██
        // ·█
        new[] { new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(1,2) },  // T4_right

        // █·
        // ██
        // █·
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(0,2) },  // T4_left

        // S4 — skew shapes, horizontal and vertical
        // ·██
        // ██·
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(1,1), new Vector2Int(2,1) },  // S4H

        // █·
        // ██
        // ·█
        new[] { new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(0,2) },  // S4V

        // Z4 — mirror of S4, horizontal and vertical
        // ██·
        // ·██
        new[] { new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(0,1), new Vector2Int(1,1) },  // Z4H

        // ·█
        // ██
        // █·
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(1,2) },  // Z4V

        // L4 — all 4 rotations
        // █·
        // █·
        // ██
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(0,2) },  // L4_0

        // █··
        // ███
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(0,1) },  // L4_1

        // ██
        // ·█
        // ·█
        new[] { new Vector2Int(1,0), new Vector2Int(1,1), new Vector2Int(0,2), new Vector2Int(1,2) },  // L4_2

        // ███
        // ··█
        new[] { new Vector2Int(2,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(2,1) },  // L4_3

        // J4 — all 4 rotations (mirror of L4)
        // ·█
        // ·█
        // ██
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(1,1), new Vector2Int(1,2) },  // J4_0

        // ███
        // █··
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(2,1) },  // J4_1

        // ██
        // █·
        // █·
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(0,2), new Vector2Int(1,2) },  // J4_2

        // ··█
        // ███
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0), new Vector2Int(2,1) },  // J4_3

        // ════════════════════════════════════════════════════════════════════
        //  5-CELL SHAPES  (2 shapes)
        //  Long bars only — seen in reference screenshots.
        // ════════════════════════════════════════════════════════════════════

        // I5 — long bars (clears an entire row/column when it fits)
        // █████
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0),
                new Vector2Int(3,0), new Vector2Int(4,0) },                                 // I5H

        // █ (5 tall)
        new[] { new Vector2Int(0,0), new Vector2Int(0,1), new Vector2Int(0,2),
                new Vector2Int(0,3), new Vector2Int(0,4) },                                 // I5V

        // ════════════════════════════════════════════════════════════════════
        //  6-CELL SHAPES  (2 shapes)
        //  Compact rectangles — seen in real Block Blast screenshots.
        // ════════════════════════════════════════════════════════════════════

        // R2x3 — 2 columns × 3 rows rectangle
        // ██
        // ██
        // ██
        new[] { new Vector2Int(0,0), new Vector2Int(1,0),
                new Vector2Int(0,1), new Vector2Int(1,1),
                new Vector2Int(0,2), new Vector2Int(1,2) },                                 // R2x3

        // R3x2 — 3 columns × 2 rows rectangle
        // ███
        // ███
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0),
                new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(2,1) },            // R3x2

        // ════════════════════════════════════════════════════════════════════
        //  9-CELL SHAPE  (1 shape)
        //  3×3 solid square — the most satisfying piece in Block Blast.
        //  Potential to clear 2 rows + 2 columns simultaneously on a full board.
        // ════════════════════════════════════════════════════════════════════

        // O9 — 3×3 square
        // ███
        // ███
        // ███
        new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0),
                new Vector2Int(0,1), new Vector2Int(1,1), new Vector2Int(2,1),
                new Vector2Int(0,2), new Vector2Int(1,2), new Vector2Int(2,2) },            // O9
    };

    // ── Validated spawn pool ─────────────────────────────────────────────────
    // Built once at class load.  Contains only shapes where Length >= MinCells.
    // GetRandom() always draws from this pool — never from All directly.
    private static readonly Vector2Int[][] _validShapes;

    static BlockShapes()
    {
        var valid = new List<Vector2Int[]>();
        var seen  = new HashSet<string>();  // duplicate detection

        for (int i = 0; i < All.Length; i++)
        {
            var shape = All[i];

            // ── MinCells check ───────────────────────────────────────────────
            if (shape.Length < MinCells)
            {
                Debug.LogWarning(
                    $"[BlockShapes] All[{i}] has {shape.Length} cell(s) " +
                    $"(< MinCells={MinCells}) — excluded from spawn pool.");
                continue;
            }

            // ── Connectivity check (4-neighbour BFS) ─────────────────────────
            if (!IsConnected(shape))
            {
                Debug.LogWarning(
                    $"[BlockShapes] All[{i}] ({shape.Length}-cell) is disconnected " +
                    $"— excluded from spawn pool.");
                continue;
            }

            // ── Duplicate check ──────────────────────────────────────────────
            string key = CanonicalKey(shape);
            if (!seen.Add(key))
            {
                Debug.LogWarning(
                    $"[BlockShapes] All[{i}] is a duplicate of an earlier shape " +
                    $"(key: {key}) — excluded from spawn pool.");
                continue;
            }

            valid.Add(shape);
        }

        if (valid.Count == 0)
            Debug.LogError("[BlockShapes] Spawn pool is empty!  " +
                           "All shapes were filtered out.  Check BlockShapes.All.");

        _validShapes = valid.ToArray();

        // Per-size breakdown for tuning / Editor verification
        int n3 = 0, n4 = 0, n5 = 0, n6 = 0, n9plus = 0;
        foreach (var s in _validShapes)
        {
            if      (s.Length == 3) n3++;
            else if (s.Length == 4) n4++;
            else if (s.Length == 5) n5++;
            else if (s.Length <= 8) n6++;
            else                    n9plus++;
        }

        Debug.Log($"[BlockShapes] Spawn pool ready: {_validShapes.Length} shapes " +
                  $"— 3-cell:{n3}  4-cell:{n4}  5-cell:{n5}  6-8-cell:{n6}  9+-cell:{n9plus}  (MinCells={MinCells}).");
    }

    // ── Public API ───────────────────────────────────────────────────────────

    // Renk paleti — ColorPalette.Blocks ile senkron (7 renk, Silver kaldırıldı).
    public static readonly Color[] Colors = ColorPalette.Blocks;

    /// Returns a random shape from the validated pool.
    /// Always >= MinCells cells; never a duplicate.
    /*public static Vector2Int[] GetRandom()
        => _validShapes[Random.Range(0, _validShapes.Length)];

*/
    public static Vector2Int[] GetRandom()
    {
        var src = _validShapes[Random.Range(0, _validShapes.Length)];
        var copy = new Vector2Int[src.Length];
        System.Array.Copy(src, copy, src.Length);
        return copy;
    }
    public static Color GetRandomColor()
        => Colors[Random.Range(0, Colors.Length)];

    // ── Private helpers ───────────────────────────────────────────────────────

    // Produces a sorted, order-independent string key for duplicate detection.
    // Two shapes are duplicates if they contain the exact same set of coordinates.
    static string CanonicalKey(Vector2Int[] shape)
    {
        var parts = new string[shape.Length];
        for (int i = 0; i < shape.Length; i++)
            parts[i] = shape[i].x + "," + shape[i].y;
        System.Array.Sort(parts, System.StringComparer.Ordinal);
        return string.Join("|", parts);
    }

    // 4-neighbour BFS connectivity check.
    // Returns false if any cell is unreachable from shape[0], which would indicate
    // a floating isolated cell — a bug in the shape definition.
    static bool IsConnected(Vector2Int[] shape)
    {
        var cellSet = new HashSet<Vector2Int>(shape);
        var visited = new HashSet<Vector2Int>();
        var queue   = new Queue<Vector2Int>();

        queue.Enqueue(shape[0]);
        visited.Add(shape[0]);

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var n in new[]
            {
                new Vector2Int(c.x + 1, c.y),
                new Vector2Int(c.x - 1, c.y),
                new Vector2Int(c.x,     c.y + 1),
                new Vector2Int(c.x,     c.y - 1),
            })
            {
                if (cellSet.Contains(n) && visited.Add(n))
                    queue.Enqueue(n);
            }
        }

        return visited.Count == shape.Length;
    }
}
