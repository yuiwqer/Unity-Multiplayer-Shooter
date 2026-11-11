// LoadingPanel.cs
// Loading UI 实现。挂在 Loading 面板 Prefab 根节点，实现 ILoadingPanel。

using UnityEngine;
using UnityEngine.UI;

public class LoadingPanel : UIPanel, ILoadingPanel
{
    [SerializeField] private Slider progressBar;
    [SerializeField] private TMPro.TextMeshProUGUI statusText;

    public void SetProgress(float progress)
    {
        if (progressBar != null) progressBar.value = Mathf.Clamp01(progress);
    }

    public void SetStatus(string status)
    {
        if (statusText != null) statusText.text = status;
    }
}