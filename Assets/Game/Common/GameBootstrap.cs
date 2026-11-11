using Unity.Netcode;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    [SerializeField] private string initialScene = "MainMenu";

    void Awake()
    {
        _ = ResourceService.Instance;
        _ = EventService.Instance;
        _ = InputService.Instance;  // 新增
        _ = SaveService.Instance;   // 新增
        _ = AudioService.Instance;
        _ = UIService.Instance;
        _ = SceneService.Instance;
        _ = ModuleService.Instance;
        _ = CameraService.Instance;
        _ = SpawnService.Instance;
    }

    void Start()
    {
        if (!string.IsNullOrEmpty(initialScene))
            SceneService.Instance.LoadScene(initialScene);
    }

    void OnDestroy()
    {
        // 退出/切场景前收尾
        ModuleService.Instance?.DisposeAllHandlers();
    }
}