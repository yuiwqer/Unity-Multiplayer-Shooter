// AudioConfigSO.cs
// 音频配置。不继承 ModuleConfigSO，由 AudioService 从 "Configs/Audio" 独立加载。
// 一个资产 = 一个可播放音频（clip + 播放参数 + 音量分组）。
//
// 命名约定：资产文件名与 audioName 一致（bgm_main.asset 的 audioName="bgm_main"）。
// 不同渠道（BGM/SFX/UI/Voice）可以复用同一个 clip，但通常拆成不同资产便于独立调参。

using UnityEngine;

public enum AudioChannel
{
    BGM = 0,
    SFX = 1,
    UI = 2,
    Voice = 3,
}

[CreateAssetMenu(fileName = "AudioConfig", menuName = "Game/Configs/Audio")]
public class AudioConfigSO : ScriptableObject
{
    [Header("标识")]
    [Tooltip("音频唯一 id。AudioService 以此为 key 查找。")]
    [SerializeField] private string audioName;

    [Tooltip("实际音频资源。")]
    [SerializeField] private AudioClip clip;

    [Header("播放参数")]
    [SerializeField] private AudioChannel channel = AudioChannel.SFX;

    [Tooltip("本音频相对于渠道音量的系数。最终音量 = 此值 × 渠道音量 × 总音量。")]
    [SerializeField, Range(0f, 1f)] private float volume = 1f;

    [Tooltip("音高随机范围。相同值表示不随机；常用 (0.95, 1.05) 让 SFX 不重复。")]
    [SerializeField] private Vector2 pitchRange = new Vector2(1f, 1f);

    [Header("BGM 专用")]
    [Tooltip("是否循环。仅 BGM 渠道有效；其他渠道忽略。")]
    [SerializeField] private bool loop = false;

    [Header("3D 音效")]
    [Tooltip("0=2D（UI/语音），1=3D（场景内音效）。3D 时 PlaySFX 需要传 position。")]
    [SerializeField, Range(0f, 1f)] private float spatialBlend = 0f;

    [SerializeField] private float minDistance = 1f;
    [SerializeField] private float maxDistance = 30f;

    public string AudioName => audioName;
    public AudioClip Clip => clip;
    public AudioChannel Channel => channel;
    public float Volume => volume;
    public Vector2 PitchRange => pitchRange;
    public bool Loop => loop;
    public float SpatialBlend => spatialBlend;
    public float MinDistance => minDistance;
    public float MaxDistance => maxDistance;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(audioName))
            Debug.LogWarning($"[{name}] AudioConfigSO: audioName 为空。", this);
        if (clip == null)
            Debug.LogWarning($"[{name}] AudioConfigSO: clip 未指定。", this);
        if (pitchRange.x > pitchRange.y)
            Debug.LogWarning($"[{name}] AudioConfigSO: pitchRange 下限大于上限。", this);
    }
#endif
}