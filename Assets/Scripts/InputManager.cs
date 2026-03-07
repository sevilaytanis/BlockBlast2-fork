using UnityEngine;

// Centralizes pointer handling and drag smoothing for both mouse and touch.
// Keeps drag feel consistent across draggable gameplay objects.
public class InputManager : MonoBehaviour
{
    public static InputManager Instance { get; private set; }

    [Header("Drag Feel")]
    [SerializeField] private float dragFollowSharpness = 18f;
    [SerializeField] private float selectedScale = 1.08f;
    [SerializeField] private float scaleSharpness = 14f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    // Reads current pointer in world space (touch-first, mouse fallback).
    public Vector3 PointerWorld()
    {
        Vector3 screenPos = Input.mousePosition;
        if (Input.touchCount > 0)
            screenPos = Input.GetTouch(0).position;

        if (Camera.main == null) return Vector3.zero;

        Vector3 world = Camera.main.ScreenToWorldPoint(screenPos);
        world.z = 0f;
        return world;
    }

    // Smoothly follows target position using frame-rate independent lerp factor.
    public Vector3 SmoothFollow(Vector3 current, Vector3 target)
    {
        float t = 1f - Mathf.Exp(-dragFollowSharpness * Time.deltaTime);
        return Vector3.Lerp(current, target, t);
    }

    // Smoothly scales object while dragging to communicate "selected" state.
    public float SmoothDragScale(float currentScale)
    {
        float t = 1f - Mathf.Exp(-scaleSharpness * Time.deltaTime);
        return Mathf.Lerp(currentScale, selectedScale, t);
    }

    // Converts a drop position to nearest board cell center for snap feel.
    public bool TryGetSnapCenter(Vector3 worldPos, out Vector2Int anchor, out Vector3 snappedCenter)
    {
        anchor = Vector2Int.zero;
        snappedCenter = worldPos;

        if (GridManager.Instance == null) return false;
        if (!GridManager.Instance.TryWorldToCell(worldPos, out anchor)) return false;

        snappedCenter = GridManager.Instance.GridToWorld(anchor.x, anchor.y);
        return true;
    }
}
