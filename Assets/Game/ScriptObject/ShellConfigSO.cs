using UnityEngine;

[CreateAssetMenu(fileName = "ShellConfig", menuName = "Game/Configs/Shell")]
public class ShellConfigSO : ModuleConfigSO
{
    [Header("弹壳资源")]
    public GameObject shellPrefab;

    [Header("物理表现")]
    public float lifeTime = 3f;       // 存活时间
    public float upForce = 4.5f;      // 向上的力
    public float rightForce = 3.5f;   // 向右的力
    public float forwardForce = 1f;   // 向前的力
}