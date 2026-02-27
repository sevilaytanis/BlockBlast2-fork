using UnityEngine;

// Very subtle tray idle breathing to make the platform feel alive.
public class TrayContainerBreath : MonoBehaviour
{
    static int s_pauseCount;

    [Range(0f, 0.04f)] public float scaleAmplitude = 0.012f;
    [Range(1f, 8f)] public float cycleSeconds = 2.6f;

    Vector3 _baseScale;
    float _phaseOffset;

    public static void PauseAll()  => s_pauseCount++;
    public static void ResumeAll() => s_pauseCount = Mathf.Max(0, s_pauseCount - 1);

    void Awake()
    {
        _baseScale = transform.localScale;
        _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
    }

    void OnEnable()
    {
        _baseScale = transform.localScale;
    }

    void Update()
    {
        if (s_pauseCount > 0)
        {
            transform.localScale = _baseScale;
            return;
        }

        if (cycleSeconds <= 0.01f) return;
        float w = (Time.time + _phaseOffset) * (2f * Mathf.PI / cycleSeconds);
        float s = 1f + Mathf.Sin(w) * scaleAmplitude;
        transform.localScale = _baseScale * s;
    }
}
