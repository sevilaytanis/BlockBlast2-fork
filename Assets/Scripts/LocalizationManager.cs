using System.Collections.Generic;
using UnityEngine;

// English-only localization (multi-language flow disabled).
public static class LocalizationManager
{
    public enum Language { English, Chinese, Spanish, Hindi, Arabic, Turkish }

    // ── String keys ───────────────────────────────────────────────────────────

    public static class Key
    {
        public const string Score      = "score";
        public const string Best       = "best";
        public const string PlayAgain  = "play_again";
        public const string GameOver   = "game_over";
        public const string MainMenu   = "main_menu";
    }

    // ── Translations ──────────────────────────────────────────────────────────

    static readonly Dictionary<string, string> _en = new Dictionary<string, string>
    {
        { Key.Score,     "Score"         },
        { Key.Best,      "Best"          },
        { Key.PlayAgain, "Play Again"    },
        { Key.GameOver,  "GAME OVER"     },
        { Key.MainMenu,  "Main Menu"     },
    };

    static readonly Dictionary<string, string> _zh = new Dictionary<string, string>
    {
        { Key.Score,     "分数"     },
        { Key.Best,      "最高分"   },
        { Key.PlayAgain, "再玩一次"  },
        { Key.GameOver,  "游戏结束"  },
        { Key.MainMenu,  "主菜单"   },
    };

    static readonly Dictionary<string, string> _es = new Dictionary<string, string>
    {
        { Key.Score,     "Puntos"          },
        { Key.Best,      "Mejor"           },
        { Key.PlayAgain, "Jugar de Nuevo"  },
        { Key.GameOver,  "FIN DEL JUEGO"   },
        { Key.MainMenu,  "Menú Principal"  },
    };

    static readonly Dictionary<string, string> _hi = new Dictionary<string, string>
    {
        { Key.Score,     "स्कोर"        },
        { Key.Best,      "सर्वश्रेष्ठ"  },
        { Key.PlayAgain, "फिर खेलें"    },
        { Key.GameOver,  "खेल खत्म"     },
        { Key.MainMenu,  "मुख्य मेनू"   },
    };

    static readonly Dictionary<string, string> _ar = new Dictionary<string, string>
    {
        { Key.Score,     "النقاط"          },
        { Key.Best,      "الأفضل"          },
        { Key.PlayAgain, "العب مجدداً"     },
        { Key.GameOver,  "انتهت اللعبة"    },
        { Key.MainMenu,  "القائمة الرئيسية" },
    };

    static readonly Dictionary<string, string> _tr = new Dictionary<string, string>
    {
        { Key.Score,     "Skor"        },
        { Key.Best,      "En İyi"      },
        { Key.PlayAgain, "Tekrar Oyna" },
        { Key.GameOver,  "OYUN BİTTİ"  },
        { Key.MainMenu,  "Ana Menü"    },
    };

    // ── Language map ──────────────────────────────────────────────────────────

    static readonly Dictionary<Language, Dictionary<string, string>> _all =
        new Dictionary<Language, Dictionary<string, string>>
    {
        { Language.English,  _en },
        { Language.Chinese,  _zh },
        { Language.Spanish,  _es },
        { Language.Hindi,    _hi },
        { Language.Arabic,   _ar },
        { Language.Turkish,  _tr },
    };

    // ── Public API ────────────────────────────────────────────────────────────

    const string PrefKey = "Language";

    static Language _current;
    public static Language Current => _current;

    // Short display code shown on the language toggle button
    public static readonly Dictionary<Language, string> Code = new Dictionary<Language, string>
    {
        { Language.English,  "EN" },
        { Language.Chinese,  "中文" },
        { Language.Spanish,  "ES" },
        { Language.Hindi,    "HI" },
        { Language.Arabic,   "AR" },
        { Language.Turkish,  "TR" },
    };

    static LocalizationManager() => _current = Language.English;

    public static string Get(string key)
    {
        // App is locked to English.
        return _en.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static void SetLanguage(Language lang)
    {
        // Multi-language disabled: always stay in English.
        if (_current != Language.English)
        {
            _current = Language.English;
            OnLanguageChanged?.Invoke();
        }
    }

    public static void CycleNext()
    {
        // Multi-language disabled: no-op.
        SetLanguage(Language.English);
    }

    public static event System.Action OnLanguageChanged;
}
