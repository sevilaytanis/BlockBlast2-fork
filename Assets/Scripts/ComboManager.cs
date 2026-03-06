using System;
using System.Collections.Generic;
using UnityEngine;

// Handles line checks, combo multiplier progression, and mega-bang event dispatch.
// Runs only when a block placement is resolved (no Update polling).
public class ComboManager : MonoBehaviour
{
    public static ComboManager Instance { get; private set; }

    [Serializable]
    public class MegaBangEventData
    {
        public int clearedLines;
        public int comboStreak;
        public float finalMultiplier;
        public int clearTier;
        public List<Vector3> clearedCellWorldPositions;
    }

    // Fired when a placement clears more than one line.
    // VFXManager can subscribe to this for impact effects.
    public event Action<MegaBangEventData> MegaBang;

    [Header("Combo")]
    [SerializeField] private float comboStepMultiplier = 0.25f;

    int _comboStreak;

    public struct LineCheckResult
    {
        public List<int> FullRows;
        public List<int> FullCols;
        public int TotalLines;
        public int CellsCleared;
        public int ClearTier; // 0=normal, 1=Big, 2=Mega, 3=Ultra
        public float BaseMultiplier;
        public List<Vector3> ClearedCellWorldPositions;

        public static LineCheckResult FromFallback(GridManager grid, List<int> fullRows, List<int> fullCols)
        {
            int cols = grid.columns;
            int rows = grid.rows;

            var uniqueCells = new HashSet<int>();
            var positions = new List<Vector3>();

            foreach (int r in fullRows)
            {
                for (int c = 0; c < cols; c++)
                {
                    int key = c * rows + r;
                    if (uniqueCells.Add(key))
                        positions.Add(grid.GridToWorld(c, r));
                }
            }

            foreach (int c in fullCols)
            {
                for (int r = 0; r < rows; r++)
                {
                    int key = c * rows + r;
                    if (uniqueCells.Add(key))
                        positions.Add(grid.GridToWorld(c, r));
                }
            }

            int cellsCleared = uniqueCells.Count;
            int clearTier = cellsCleared <= 8 ? 0 : cellsCleared <= 15 ? 1 : cellsCleared <= 21 ? 2 : 3;
            float baseMultiplier = clearTier == 0 ? 1f : clearTier == 1 ? 2f : clearTier == 2 ? 3f : 4f;

            return new LineCheckResult
            {
                FullRows = fullRows,
                FullCols = fullCols,
                TotalLines = fullRows.Count + fullCols.Count,
                CellsCleared = cellsCleared,
                ClearTier = clearTier,
                BaseMultiplier = baseMultiplier,
                ClearedCellWorldPositions = positions
            };
        }
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()
    {
        GameManager.OnGameOver += ResetCombo;
    }

    void OnDisable()
    {
        GameManager.OnGameOver -= ResetCombo;
    }

    // Scans the board snapshot and returns full rows/columns and derived clear metadata.
    public LineCheckResult CheckLines(GridManager grid)
    {
        bool[,] board = grid.GetBoardSnapshot();
        int cols = grid.columns;
        int rows = grid.rows;

        var fullRows = new List<int>();
        var fullCols = new List<int>();

        for (int r = 0; r < rows; r++)
        {
            bool isFull = true;
            for (int c = 0; c < cols; c++)
            {
                if (!board[c, r]) { isFull = false; break; }
            }
            if (isFull) fullRows.Add(r);
        }

        for (int c = 0; c < cols; c++)
        {
            bool isFull = true;
            for (int r = 0; r < rows; r++)
            {
                if (!board[c, r]) { isFull = false; break; }
            }
            if (isFull) fullCols.Add(c);
        }

        return LineCheckResult.FromFallback(grid, fullRows, fullCols);
    }

    // Applies score after clear resolution; updates combo streak and raises mega-bang event.
    public void OnMoveResolved(int placedSquares, LineCheckResult result)
    {
        if (result.TotalLines <= 0)
        {
            ResetCombo();
            GameManager.Instance?.AddScore(placedSquares, 0, 1f);
            return;
        }

        _comboStreak++;

        float comboMultiplier = 1f + Mathf.Max(0, _comboStreak - 1) * comboStepMultiplier;
        float finalMultiplier = result.BaseMultiplier * comboMultiplier;

        GameManager.Instance?.AddScore(placedSquares, result.TotalLines, finalMultiplier);

        if (result.TotalLines > 1)
        {
            MegaBang?.Invoke(new MegaBangEventData
            {
                clearedLines = result.TotalLines,
                comboStreak = _comboStreak,
                finalMultiplier = finalMultiplier,
                clearTier = result.ClearTier,
                clearedCellWorldPositions = result.ClearedCellWorldPositions
            });
        }
    }

    // Public reset helper for external game-state transitions.
    public void ResetCombo()
    {
        _comboStreak = 0;
    }
}
