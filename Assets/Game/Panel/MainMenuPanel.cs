// MainMenuPanel.cs
// 主菜单面板。负责按钮点击和面板生命周期。
using UnityEngine;
using UnityEngine.UI;

public class MainMenuPanel : UIPanel
{
    [Header("按钮绑定")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button settingsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;

    protected override void OnPanelInitialize()
    {
        // 只在首次创建时绑定一次
        startButton.onClick.AddListener(OnStartClicked);
        settingsButton.onClick.AddListener(OnSettingsClicked);
        quitButton.onClick.AddListener(OnQuitClicked);
        
        //
        hostButton.onClick.AddListener(OnStartHostClicked);
        joinButton.onClick.AddListener(OnJoinClientClicked);
    }

    private void OnStartClicked()
    {
        // 播放按钮音效（如果接了 AudioService）
        AudioService.Instance?.PlayUI("click_01");
        
        // 切换到游戏场景
        Debug.Log("点击了开始游戏，准备切场景...");
        SceneService.Instance.LoadScene("GameScene"); // 等你有游戏场景了再解开
        UIService.Instance.Close("main_menu");
        
        // 目前如果没游戏场景，先打印一条日志或干点别的
    }

    private void OnSettingsClicked()
    {
        AudioService.Instance?.PlayUI("click_01");
        // 打开设置面板（由 UIService 管理）
        UIService.Instance.Open("settings");
    }

    private void OnQuitClicked()
    {
        AudioService.Instance?.PlayUI("click_01");
        Debug.Log("退出游戏");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
    // 按钮1：开启主机 (Host)
    public void OnStartHostClicked()
    {
        // 调用 NetworkService 启动 Host
        if (NetworkService.Instance.StartHost())
        {
            Debug.Log("作为主机启动成功！");
            // 启动成功后，加载游戏场景
            SceneService.Instance.LoadScene("GameScene");
            UIService.Instance.Close("main_menu");
        }
    }

    // 按钮2：加入游戏 (Client)
    public void OnJoinClientClicked()
    {
        // 调用 NetworkService 启动 Client
        if (NetworkService.Instance.StartClient())
        {
            UIService.Instance.Close("main_menu");
            Debug.Log("客户端连接中...");
            // 注意：客户端连上后，通常不需要手动切场景，等服务器切场景同步过去，或者等连上后再切
        }
    }
}