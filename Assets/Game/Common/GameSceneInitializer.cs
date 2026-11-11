using Unity.Netcode;
using UnityEngine;

public class GameSceneInitializer : MonoBehaviour
{
    void Start()
    {
        // 1. 打开 UI
        UIService.Instance.Open("hud_ammo");
        UIService.Instance.Open("hud_crosshair");

        // 2. 服务器负责初始化网络对象
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
        {
            // 2.1 生成玩家
            SpawnService.Instance.SpawnAllConnectedPlayers();
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;

            // 2.2 生成初始靶子
            SpawnInitialTargets();
        }
    }

    private void SpawnInitialTargets()
    {
        // 在场景中心点前方生成 3 个靶子
        ModuleService.Instance.SpawnHandler("enemy_dummy", new Vector3(0, 1f, 10f));
        ModuleService.Instance.SpawnHandler("enemy_dummy", new Vector3(3, 1f, 12f));
        ModuleService.Instance.SpawnHandler("enemy_dummy", new Vector3(-3, 1f, 12f));
        
        Debug.Log("[场景] 初始靶子已生成！");
    }

    private void HandleClientConnected(ulong clientId)
    {
        SpawnService.Instance.SpawnPlayerForClient(clientId);
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
        }
    }
}