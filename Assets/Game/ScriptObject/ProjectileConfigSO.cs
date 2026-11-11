using System;
using UnityEngine;

[CreateAssetMenu(fileName = "ProjectileConfig", menuName = "Game/Configs/Projectile")]
public class ProjectileConfigSO : ModuleConfigSO
{
    public float speed = 20f;
    public float damage = 10f;
    public float lifetime = 3f;

    // ★ 必须有这一句！告诉系统这个 SO 对应的是 ProjectileHandler
    public override Type HandlerType => typeof(ProjectileHandler); 
}