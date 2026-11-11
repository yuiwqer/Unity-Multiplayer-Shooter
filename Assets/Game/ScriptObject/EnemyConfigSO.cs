using UnityEngine;

[CreateAssetMenu(fileName = "EnemyConfig", menuName = "Game/Configs/Enemy")]
public class EnemyConfigSO : ModuleConfigSO
{
    [Header("属性")]
    public float maxHealth = 100f;
    
    [Header("表现")]
    public Color hitFlashColor = Color.red;
    public float hitFlashDuration = 0.1f;
    public GameObject deathEffectPrefab;

    [Header("重生机制")]
    [Tooltip("死亡后是否自动生成一个新的自己")]
    public bool respawnOnDeath = true;
    [Tooltip("死亡后多少秒生成新靶子")]
    public float respawnDelay = 2f;
    [Tooltip("重生位置的随机半径")]
    public float respawnRadius = 3f;

    public override System.Type HandlerType => typeof(EnemyHandler);
}