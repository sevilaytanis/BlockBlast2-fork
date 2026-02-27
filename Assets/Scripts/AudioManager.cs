using UnityEngine;

// Centralised sound effects.
// Attach to an empty GameObject named "AudioManager".
// Assign AudioClip fields in the Inspector (leave empty to silence that effect).
public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }

    [Header("Sound Effects")]
    public AudioClip placeClip;    // block lands on grid
    public AudioClip clearClip;    // row / column cleared
    public AudioClip invalidClip;  // block returned to tray
    public AudioClip gameOverClip; // game over

    AudioSource _source;

    void Awake()
    {
        Instance            = this;
        _source             = gameObject.AddComponent<AudioSource>();
        _source.playOnAwake = false;
    }

    public void PlayPlace()    => Play(placeClip);
    public void PlayClear()    => Play(clearClip);
    public void PlayInvalid()  => Play(invalidClip);
    public void PlayGameOver() => Play(gameOverClip);

    // Generic hook used by gameplay scripts.
    public void PlaySfx(string sfxId) => Play(sfxId);

    public void Play(string sfxId)
    {
        switch (sfxId)
        {
            case "place":
                PlayClip(placeClip);
                break;
            case "clear":
                PlayClip(clearClip);
                break;
            case "invalid":
                PlayClip(invalidClip);
                break;
            case "gameOver":
                PlayClip(gameOverClip);
                break;
        }
    }

    void Play(AudioClip clip) => PlayClip(clip);

    void PlayClip(AudioClip clip)
    {
        if (clip != null) _source.PlayOneShot(clip);
    }
}
