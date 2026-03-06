using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Listens to ComboManager mega-bang events and drives impact VFX.
// Includes camera shake, pooled particles, hitstop, and global grid flash pulse.
public class VFXManager : MonoBehaviour
{
    public static VFXManager Instance { get; private set; }

    [Header("Camera Shake")]
    [SerializeField] private float shakeDuration = 0.30f;
    [SerializeField] private float shakeBaseIntensity = 0.08f;
    [SerializeField] private float shakePerLine = 0.03f;
    [SerializeField] private float shakeMaxIntensity = 0.28f;

    [Header("Hitstop")]
    [SerializeField] private float hitstopDuration = 0.05f;

    [Header("Particles")]
    [SerializeField] private ParticleSystem crystalBurstPrefab;
    [SerializeField] private int initialPoolSize = 24;
    [SerializeField] private int maxBurstsPerMegaBang = 64;

    [Header("Grid Glow Pulse")]
    [SerializeField] private string shaderFlashProperty = "_MegaBangFlash";
    [SerializeField] private float flashPeak = 1f;
    [SerializeField] private float flashDuration = 0.18f;

    readonly Queue<ParticleSystem> _particlePool = new Queue<ParticleSystem>();
    Coroutine _shakeRoutine;
    Coroutine _flashRoutine;
    Transform _cam;
    Vector3 _camBasePos;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (Camera.main != null)
        {
            _cam = Camera.main.transform;
            _camBasePos = _cam.position;
        }

        WarmPool();
    }

    void OnEnable()
    {
        StartCoroutine(BindToComboManager());
    }

    void OnDisable()
    {
        if (ComboManager.Instance != null)
            ComboManager.Instance.MegaBang -= HandleMegaBang;

        if (_cam != null)
            _cam.position = _camBasePos;

        Shader.SetGlobalFloat(shaderFlashProperty, 0f);
    }

    IEnumerator BindToComboManager()
    {
        while (ComboManager.Instance == null)
            yield return null;

        ComboManager.Instance.MegaBang -= HandleMegaBang;
        ComboManager.Instance.MegaBang += HandleMegaBang;
    }

    void HandleMegaBang(ComboManager.MegaBangEventData data)
    {
        if (_cam == null && Camera.main != null)
        {
            _cam = Camera.main.transform;
            _camBasePos = _cam.position;
        }

        float intensity = Mathf.Min(shakeBaseIntensity + data.clearedLines * shakePerLine, shakeMaxIntensity);

        if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
        _shakeRoutine = StartCoroutine(CameraShakeRoutine(intensity));

        if (_flashRoutine != null) StopCoroutine(_flashRoutine);
        _flashRoutine = StartCoroutine(GridFlashRoutine());

        StartCoroutine(HitstopRoutine());
        SpawnBursts(data.clearedCellWorldPositions);
    }

    // Briefly pauses gameplay to increase impact.
    IEnumerator HitstopRoutine()
    {
        float prev = Time.timeScale;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(hitstopDuration);
        Time.timeScale = prev <= 0f ? 1f : prev;
    }

    // Decaying random camera shake.
    IEnumerator CameraShakeRoutine(float intensity)
    {
        if (_cam == null) yield break;

        _camBasePos = _cam.position;
        float elapsed = 0f;

        while (elapsed < shakeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float decay = 1f - (elapsed / shakeDuration);
            Vector2 offset = Random.insideUnitCircle * intensity * decay;
            _cam.position = _camBasePos + new Vector3(offset.x, offset.y, 0f);
            yield return null;
        }

        _cam.position = _camBasePos;
        _shakeRoutine = null;
    }

    // Sends a short global shader pulse for grid flash materials.
    IEnumerator GridFlashRoutine()
    {
        float half = flashDuration * 0.5f;
        float t = 0f;

        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float v = Mathf.Lerp(0f, flashPeak, t / Mathf.Max(0.0001f, half));
            Shader.SetGlobalFloat(shaderFlashProperty, v);
            yield return null;
        }

        t = 0f;
        while (t < half)
        {
            t += Time.unscaledDeltaTime;
            float v = Mathf.Lerp(flashPeak, 0f, t / Mathf.Max(0.0001f, half));
            Shader.SetGlobalFloat(shaderFlashProperty, v);
            yield return null;
        }

        Shader.SetGlobalFloat(shaderFlashProperty, 0f);
        _flashRoutine = null;
    }

    void WarmPool()
    {
        if (crystalBurstPrefab == null) return;

        for (int i = 0; i < initialPoolSize; i++)
        {
            var ps = Instantiate(crystalBurstPrefab, transform);
            ps.gameObject.SetActive(false);
            _particlePool.Enqueue(ps);
        }
    }

    void SpawnBursts(List<Vector3> worldPositions)
    {
        if (crystalBurstPrefab == null || worldPositions == null || worldPositions.Count == 0) return;

        int count = Mathf.Min(worldPositions.Count, maxBurstsPerMegaBang);
        for (int i = 0; i < count; i++)
        {
            ParticleSystem ps = GetPooled();
            ps.transform.position = worldPositions[i];
            ps.gameObject.SetActive(true);
            ps.Play(true);

            float life = ps.main.duration;
            if (ps.main.startLifetime.mode == ParticleSystemCurveMode.Constant)
                life += ps.main.startLifetime.constant;

            StartCoroutine(ReleaseAfter(ps, life + 0.1f));
        }
    }

    ParticleSystem GetPooled()
    {
        if (_particlePool.Count > 0)
            return _particlePool.Dequeue();

        var ps = Instantiate(crystalBurstPrefab, transform);
        ps.gameObject.SetActive(false);
        return ps;
    }

    IEnumerator ReleaseAfter(ParticleSystem ps, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);

        if (ps == null) yield break;

        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.gameObject.SetActive(false);
        _particlePool.Enqueue(ps);
    }
}
