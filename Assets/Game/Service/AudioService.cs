// AudioService.cs
// 音频服务：BGM（双源交叉淡入淡出）、SFX（round-robin 池）、UI、Voice。
//
// 设计要点：
//   1. 配置从 "Configs/Audio" 独立加载，不走 ModuleService。
//   2. 音量分四组（Master / BGM / SFX / UI / Voice），最终音量 = config.Volume × 渠道音量 × 总音量。
//      音量值持久化到 PlayerPrefs，下次启动自动恢复。
//   3. BGM 用双 AudioSource 交叉淡入淡出：新曲在 inactive 源上播放，协程内同步调节两源音量，结束时交换引用。
//   4. SFX 用 16 个 AudioSource 的 round-robin 池，按需设置 position / spatialBlend / pitch。
//      UI / Voice 共用 _uiSource（2D）。
//   5. 所有 AudioSource 挂在 AudioService 自身节点下，DontDestroyOnLoad 保证切场景不断。
//
// 调用方：
//   AudioService.Instance.PlayBGM("bgm_main");
//   AudioService.Instance.PlaySFX("hit_metal", hitPoint);
//   AudioService.Instance.PlayUI("click_01");
//   AudioService.Instance.SetVolume(AudioChannel.BGM, 0.6f);

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AudioService : MonoBehaviour
{
    #region 单例

    private static AudioService _instance;
    private static readonly object _lock = new object();
    private static bool _quitting;

    public static AudioService Instance
    {
        get
        {
            if (_quitting) return null;

            lock (_lock)
            {
                if (_instance != null) return _instance;

                _instance = FindExisting();
                if (_instance != null) return _instance;

                var go = new GameObject($"[{nameof(AudioService)}]");
                _instance = go.AddComponent<AudioService>();
                DontDestroyOnLoad(go);
                return _instance;
            }
        }
    }

    private static AudioService FindExisting()
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<AudioService>(FindObjectsInactive.Include);
#else
        return FindObjectOfType<AudioService>();
#endif
    }

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(gameObject);

        SetupSources();
        LoadVolumes();
        TryInitialize();
    }

    void OnApplicationQuit() => _quitting = true;

    void OnDestroy()
    {
        if (_instance != this) return;
        _configMap.Clear();
        _instance = null;
    }

    #endregion

    #region 配置与常量

    private const string PrefsPrefix = "audio.vol.";
    private const int SfxPoolSize = 16;

    private bool _initialized;
    private readonly Dictionary<string, AudioConfigSO> _configMap = new Dictionary<string, AudioConfigSO>(64);

    #endregion

    #region AudioSource 布置

    private AudioSource _bgmA;
    private AudioSource _bgmB;
    private AudioSource _bgmActive;      // 当前主源
    private AudioSource _bgmInactive;    // 淡入备用源

    private AudioSource _uiSource;       // UI / Voice 共用（2D）
    private AudioSource[] _sfxSources;   // round-robin 池
    private int _sfxIndex;

    private Coroutine _bgmFadeCoroutine;

    private void SetupSources()
    {
        _bgmA = CreateSource("BGM_A", loop: true, spatialBlend: 0f);
        _bgmB = CreateSource("BGM_B", loop: true, spatialBlend: 0f);
        _bgmActive = _bgmA;
        _bgmInactive = _bgmB;

        _uiSource = CreateSource("UI", loop: false, spatialBlend: 0f);

        _sfxSources = new AudioSource[SfxPoolSize];
        for (int i = 0; i < SfxPoolSize; i++)
            _sfxSources[i] = CreateSource($"SFX_{i:00}", loop: false, spatialBlend: 1f);

        _sfxIndex = 0;
    }

    private AudioSource CreateSource(string name, bool loop, float spatialBlend)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = loop;
        src.spatialBlend = spatialBlend;
        return src;
    }

    #endregion

    #region 初始化

    private void TryInitialize()
    {
        if (_initialized) return;
        if (ResourceService.Instance == null)
        {
            Debug.LogWarning("[AudioService] ResourceService 未就绪，初始化延后。");
            return;
        }

        var configs = ResourceService.Instance.LoadAll<AudioConfigSO>("Configs/Audio");
        if (configs != null)
        {
            foreach (var cfg in configs)
            {
                if (cfg == null || string.IsNullOrWhiteSpace(cfg.AudioName)) continue;
                if (_configMap.ContainsKey(cfg.AudioName))
                {
                    Debug.LogError($"[AudioService] 音频 id 重复：{cfg.AudioName}", cfg);
                    continue;
                }
                _configMap[cfg.AudioName] = cfg;
            }
        }

        _initialized = true;
    }

    #endregion

    #region 音量

    private float _masterVolume = 1f;
    private float _bgmVolume = 1f;
    private float _sfxVolume = 1f;
    private float _uiVolume = 1f;
    private float _voiceVolume = 1f;

    private void LoadVolumes()
    {
        _masterVolume = PlayerPrefs.GetFloat(PrefsPrefix + "master", 1f);
        _bgmVolume    = PlayerPrefs.GetFloat(PrefsPrefix + "bgm", 1f);
        _sfxVolume    = PlayerPrefs.GetFloat(PrefsPrefix + "sfx", 1f);
        _uiVolume     = PlayerPrefs.GetFloat(PrefsPrefix + "ui", 1f);
        _voiceVolume  = PlayerPrefs.GetFloat(PrefsPrefix + "voice", 1f);
    }

    /// <summary>设置某渠道音量（0~1）。BGM 音量会立即应用到当前播放的源。</summary>
    public void SetVolume(AudioChannel channel, float volume)
    {
        volume = Mathf.Clamp01(volume);
        switch (channel)
        {
            case AudioChannel.BGM:   _bgmVolume = volume;   break;
            case AudioChannel.SFX:   _sfxVolume = volume;   break;
            case AudioChannel.UI:    _uiVolume = volume;    break;
            case AudioChannel.Voice: _voiceVolume = volume; break;
        }
        PlayerPrefs.SetFloat(PrefsPrefix + channel.ToString().ToLowerInvariant(), volume);
        PlayerPrefs.Save();

        // BGM 音量变化立即生效
        if (channel == AudioChannel.BGM && _bgmActive != null && _bgmActive.isPlaying)
            _bgmActive.volume = GetEffectiveVolume(_bgmActive.clip != null ? GetConfigByClip(_bgmActive.clip) : null, AudioChannel.BGM);
    }

    public void SetMasterVolume(float volume)
    {
        _masterVolume = Mathf.Clamp01(volume);
        PlayerPrefs.SetFloat(PrefsPrefix + "master", _masterVolume);
        PlayerPrefs.Save();

        if (_bgmActive != null && _bgmActive.isPlaying)
        {
            var cfg = _bgmActive.clip != null ? GetConfigByClip(_bgmActive.clip) : null;
            _bgmActive.volume = GetEffectiveVolume(cfg, AudioChannel.BGM);
        }
    }

    public float GetVolume(AudioChannel channel)
    {
        return channel switch
        {
            AudioChannel.BGM   => _bgmVolume,
            AudioChannel.SFX   => _sfxVolume,
            AudioChannel.UI    => _uiVolume,
            AudioChannel.Voice => _voiceVolume,
            _ => 1f,
        };
    }

    public float MasterVolume => _masterVolume;

    private float GetChannelVolume(AudioChannel channel)
    {
        return channel switch
        {
            AudioChannel.BGM   => _bgmVolume,
            AudioChannel.SFX   => _sfxVolume,
            AudioChannel.UI    => _uiVolume,
            AudioChannel.Voice => _voiceVolume,
            _ => 1f,
        };
    }

    private float GetEffectiveVolume(AudioConfigSO cfg, AudioChannel channel)
    {
        var baseVol = cfg != null ? cfg.Volume : 1f;
        return baseVol * GetChannelVolume(channel) * _masterVolume;
    }

    // 反查 clip → config，用于 BGM 音量实时调节。BGM 数量少，线性扫描可接受。
    private AudioConfigSO GetConfigByClip(AudioClip clip)
    {
        foreach (var cfg in _configMap.Values)
            if (cfg != null && cfg.Clip == clip) return cfg;
        return null;
    }

    #endregion

    #region BGM

    /// <summary>播放 BGM。相同曲目重复调用不会重播。fadeTime &gt; 0 时交叉淡入淡出。</summary>
    public void PlayBGM(string audioName, float fadeTime = 0.5f)
    {
        if (!_initialized) { Debug.LogError("[AudioService] 未初始化就调用 PlayBGM。"); return; }
        if (!_configMap.TryGetValue(audioName, out var cfg))
        {
            Debug.LogError($"[AudioService] 未找到 BGM 配置：{audioName}");
            return;
        }

        // 相同曲目已在播，忽略
        if (_bgmActive.isPlaying && _bgmActive.clip == cfg.Clip) return;

        if (_bgmFadeCoroutine != null)
        {
            StopCoroutine(_bgmFadeCoroutine);
            _bgmFadeCoroutine = null;
        }

        _bgmInactive.clip = cfg.Clip;
        _bgmInactive.loop = cfg.Loop;
        _bgmInactive.pitch = 1f;
        _bgmInactive.volume = 0f;
        _bgmInactive.Play();

        if (fadeTime <= 0f)
        {
            _bgmActive.Stop();
            _bgmInactive.volume = GetEffectiveVolume(cfg, AudioChannel.BGM);
            SwapBgmSources();
        }
        else
        {
            _bgmFadeCoroutine = StartCoroutine(CrossFadeBgm(cfg, fadeTime));
        }
    }

    public void StopBGM(float fadeTime = 0.5f)
    {
        if (_bgmFadeCoroutine != null)
        {
            StopCoroutine(_bgmFadeCoroutine);
            _bgmFadeCoroutine = null;
        }

        if (fadeTime <= 0f || !_bgmActive.isPlaying)
        {
            _bgmActive.Stop();
            _bgmInactive.Stop();
            return;
        }

        _bgmFadeCoroutine = StartCoroutine(FadeOutAndStop(fadeTime));
    }

    private IEnumerator CrossFadeBgm(AudioConfigSO newCfg, float duration)
    {
        var targetVol = GetEffectiveVolume(newCfg, AudioChannel.BGM);
        var startVol = _bgmActive.isPlaying ? _bgmActive.volume : 0f;
        var elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / duration);

            _bgmInactive.volume = Mathf.Lerp(0f, targetVol, t);
            _bgmActive.volume   = Mathf.Lerp(startVol, 0f, t);
            yield return null;
        }

        _bgmActive.Stop();
        _bgmInactive.volume = targetVol;
        SwapBgmSources();
        _bgmFadeCoroutine = null;
    }

    private IEnumerator FadeOutAndStop(float duration)
    {
        var startVol = _bgmActive.volume;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(elapsed / duration);
            _bgmActive.volume = Mathf.Lerp(startVol, 0f, t);
            yield return null;
        }
        _bgmActive.Stop();
        _bgmFadeCoroutine = null;
    }

    private void SwapBgmSources()
    {
        var tmp = _bgmActive;
        _bgmActive = _bgmInactive;
        _bgmInactive = tmp;
    }

    #endregion

    #region SFX / UI / Voice

    /// <summary>播放 3D 音效（SFX 渠道）。position 为世界坐标；不传则在原点。</summary>
    public void PlaySFX(string audioName, Vector3? position = null)
    {
        if (!_initialized) { Debug.LogError("[AudioService] 未初始化就调用 PlaySFX。"); return; }
        if (!_configMap.TryGetValue(audioName, out var cfg))
        {
            Debug.LogError($"[AudioService] 未找到 SFX 配置：{audioName}");
            return;
        }

        var src = GetNextSfxSource();
        ApplySfxConfig(src, cfg, position);
        src.Play();
    }

    /// <summary>播放 2D UI 音效（UI 渠道）。</summary>
    public void PlayUI(string audioName)
    {
        if (!_initialized) { Debug.LogError("[AudioService] 未初始化就调用 PlayUI。"); return; }
        if (!_configMap.TryGetValue(audioName, out var cfg))
        {
            Debug.LogError($"[AudioService] 未找到 UI 配置：{audioName}");
            return;
        }

        _uiSource.pitch = RandomPitch(cfg);
        _uiSource.volume = GetEffectiveVolume(cfg, AudioChannel.UI);
        _uiSource.spatialBlend = 0f;
        _uiSource.PlayOneShot(cfg.Clip);
    }

    /// <summary>播放语音（Voice 渠道，2D）。</summary>
    public void PlayVoice(string audioName)
    {
        if (!_initialized) { Debug.LogError("[AudioService] 未初始化就调用 PlayVoice。"); return; }
        if (!_configMap.TryGetValue(audioName, out var cfg))
        {
            Debug.LogError($"[AudioService] 未找到 Voice 配置：{audioName}");
            return;
        }

        _uiSource.pitch = RandomPitch(cfg);
        _uiSource.volume = GetEffectiveVolume(cfg, AudioChannel.Voice);
        _uiSource.spatialBlend = 0f;
        _uiSource.PlayOneShot(cfg.Clip);
    }

    private AudioSource GetNextSfxSource()
    {
        var src = _sfxSources[_sfxIndex];
        _sfxIndex = (_sfxIndex + 1) % _sfxSources.Length;
        if (src.isPlaying) src.Stop();
        return src;
    }

    private void ApplySfxConfig(AudioSource src, AudioConfigSO cfg, Vector3? position)
    {
        src.clip = cfg.Clip;
        src.pitch = RandomPitch(cfg);
        src.volume = GetEffectiveVolume(cfg, AudioChannel.SFX);
        src.spatialBlend = cfg.SpatialBlend;
        src.minDistance = cfg.MinDistance;
        src.maxDistance = cfg.MaxDistance;

        if (cfg.SpatialBlend > 0f)
        {
            var pos = position ?? Vector3.zero;
            src.transform.position = pos;
        }
        else
        {
            src.transform.localPosition = Vector3.zero;
        }
    }

    private float RandomPitch(AudioConfigSO cfg)
    {
        var r = cfg.PitchRange;
        return r.x == r.y ? r.x : UnityEngine.Random.Range(r.x, r.y);
    }

    #endregion

    #region 调试

    public void DumpAudio()
    {
        Debug.Log($"[AudioService] configs={_configMap.Count} bgm={( _bgmActive.clip != null ? _bgmActive.clip.name : "none")} playing={_bgmActive.isPlaying}");
        Debug.Log($"  master={_masterVolume:F2} bgm={_bgmVolume:F2} sfx={_sfxVolume:F2} ui={_uiVolume:F2} voice={_voiceVolume:F2}");
    }

    #endregion
}