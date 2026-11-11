using Game.Events;
using UnityEngine;
using TMPro; // 如果你用的是旧版 Text，改为 using UnityEngine.UI;

public class HUD_AmmoPanel : UIPanel
{
    [Header("UI 引用")]
    [SerializeField] private TextMeshProUGUI ammoText; 
    // 如果用旧版：private Text ammoText;

    protected override void OnPanelInitialize()
    {
        // 订阅弹药变化事件
        Subscribe<AmmoChangedEvent>(OnAmmoChanged);
    }

    protected override void OnPanelDispose()
    {
        // 退订
        Unsubscribe<AmmoChangedEvent>(OnAmmoChanged);
    }

    private void OnAmmoChanged(AmmoChangedEvent evt)
    {
        // 更新文字，例如显示 "30 / 30"
        if (ammoText != null)
        {
            ammoText.text = $"{evt.CurrentAmmo} <size=50%> / {evt.MaxAmmo}</size>";
        }
    }
}