using System;
using UnityEngine;

[CreateAssetMenu(fileName = "WeaponConfig", menuName = "Game/Configs/Weapon")]
public class WeaponConfigSO : ModuleConfigSO
{
    [Header("武器属性")]
    public float fireRate = 5f;
    public GameObject projectilePrefab;
    public ProjectileConfigSO projectileConfig;

    [Header("弹药与换弹")]
    public int magazineSize = 30;
    public float reloadTime = 1.5f;

    [Header("表现与抛射物 (SO)")]
    [Tooltip("开火时抛出的小弹壳")]
    public ShellConfigSO shellConfig; 
    
    [Tooltip("换弹时抛出的大弹夹")]
    public ShellConfigSO magazineConfig; // ★ 也改成 SO

    public override Type HandlerType => typeof(WeaponHandler);
}