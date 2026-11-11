// SettingsPanel.cs
using UnityEngine;
using UnityEngine.UI;

public class SettingsPanel : UIPanel
{
    [Header("UI 引用")]
    [SerializeField] private Slider bgmSlider;
    [SerializeField] private Slider sfxSlider;
    [SerializeField] private Button closeButton;

    protected override void OnPanelInitialize()
    {
        // 绑定按钮和滑动条事件
        closeButton.onClick.AddListener(() => UIService.Instance.Close(PanelName));
        
        bgmSlider.onValueChanged.AddListener(OnBgmChanged);
        sfxSlider.onValueChanged.AddListener(OnSfxChanged);
    }

    protected override void OnPanelOpen(object data)
    {
        // 每次打开时，从 AudioService 读取当前音量并同步到 UI
        // 使用 SetValueWithoutNotify 防止触发 onValueChanged 导致死循环
        bgmSlider.SetValueWithoutNotify(AudioService.Instance.GetVolume(AudioChannel.BGM));
        sfxSlider.SetValueWithoutNotify(AudioService.Instance.GetVolume(AudioChannel.SFX));
    }

    private void OnBgmChanged(float value)
    {
        AudioService.Instance.SetVolume(AudioChannel.BGM, value);
    }

    private void OnSfxChanged(float value)
    {
        AudioService.Instance.SetVolume(AudioChannel.SFX, value);
        // 拖动时顺便播个音效测试一下（可选）
        if (Input.GetMouseButton(0)) 
            AudioService.Instance.PlayUI("click_01"); 
    }
}