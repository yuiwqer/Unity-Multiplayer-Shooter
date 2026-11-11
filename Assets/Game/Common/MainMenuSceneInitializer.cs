// MainMenuSceneInitializer.cs
using UnityEngine;

public class MainMenuSceneInitializer : MonoBehaviour
{
    void Start()
    {
        // 场景加载完成后，打开主菜单 UI
        // 注意传入的名字必须与 SO 的 Panel Name 完全一致
        UIService.Instance.Open("main_menu");
    }
}