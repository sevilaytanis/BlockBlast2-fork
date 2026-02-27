using UnityEngine;
using UnityEngine.SceneManagement;

public enum GameMode { Normal, Challenge }

// Top-level controller: scoring, game-over, mode architecture.
// Attach to an empty GameObject named "GameManager".
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Game Mode")]
    public GameMode gameMode = GameMode.Normal;

    [Header("Scoring")]
    public int pointsPerSquare = 10;   // per square placed
    public int pointsPerLine   = 100;  // per row/column cleared
    public int comboBonus      = 50;   // extra per line beyond the first in one move

    // ── Runtime state ─────────────────────────────────────────────────────────

    public int  Score      { get; private set; }
    public int  HighScore  { get; private set; }
    public bool IsGameOver { get; private set; }

    // ── Events (UI / future systems subscribe here) ───────────────────────────

    public static event System.Action<int>         OnScoreChanged;
    public static event System.Action<int>         OnHighScoreChanged;
    public static event System.Action              OnGameOver;
    // delta = points earned this move, worldPos = where to spawn the popup
    public static event System.Action<int, Vector3> OnScorePopup;

    const string HighScoreKey = "HighScore_Normal";

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Awake()
    {
        Instance  = this;
        HighScore = PlayerPrefs.GetInt(HighScoreKey, 0);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void AddScore(int squaresPlaced, int linesCleared, float multiplier = 1f)
    {
        if (IsGameOver) return;

        int pts = squaresPlaced * pointsPerSquare;

        if (linesCleared > 0)
        {
            pts += linesCleared * pointsPerLine;
            if (linesCleared > 1)
                pts += (linesCleared - 1) * comboBonus; // combo bonus
        }

        pts = Mathf.RoundToInt(pts * multiplier);

        Score += pts;
        OnScoreChanged?.Invoke(Score);

        // Fire popup event so UIManager can show a floating "+N" label
        if (pts > 0 && GridManager.Instance != null)
        {
            var g = GridManager.Instance;
            var popupPos = new Vector3(
                g.origin.x + (g.columns - 1) * g.cellSize * 0.5f,
                g.origin.y + (g.rows - 1)    * g.cellSize * 0.5f,
                0f);
            OnScorePopup?.Invoke(pts, popupPos);
        }

        if (Score > HighScore)
        {
            HighScore = Score;
            PlayerPrefs.SetInt(HighScoreKey, HighScore);
            PlayerPrefs.Save();
            OnHighScoreChanged?.Invoke(HighScore);
        }
    }

    public void TriggerGameOver()
    {
        if (IsGameOver) return;
        IsGameOver = true;
        AudioManager.Instance?.PlayGameOver();
        OnGameOver?.Invoke();
    }

    public void RestartGame()
    {
        // Load by name - reliable even when scene is not added to Build Settings yet.
        // Static events are unsubscribed in UIManager.OnDisable before the reload.
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    // ── Editor test helper ────────────────────────────────────────────────────

    [ContextMenu("Test: Trigger Game Over")]
    void TestGameOver() => TriggerGameOver();
}
