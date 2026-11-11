using Unity.Netcode;
using UnityEngine;

public class ProjectileHandler : NetworkModuleHandler
{
    private ProjectileConfigSO _config;
    private Vector3 _direction;
    private float _lifeTimer;
    private bool _isLaunched;

    protected override void OnModuleInitialize()
    {
        _config = Config as ProjectileConfigSO;
    }

    public void Launch(Vector3 direction)
    {
        _direction = direction.normalized;
        _lifeTimer = _config.lifetime;
        _isLaunched = true;
    }

    public override void OnModuleUpdate(float deltaTime)
    {
        // ★ 核心：子弹的物理移动和伤害判定，全由服务器负责
        if (!IsServer || !_isLaunched) return;

        float moveDistance = _config.speed * deltaTime;
        
        // 射线检测防穿透
        Vector3 rayStart = transform.position + _direction * 0.1f; // 稍微偏前防卡自己
        if (Physics.Raycast(rayStart, _direction, out RaycastHit hit, moveDistance))
        {
            // 忽略玩家自己的碰撞体（或者根据 LayerMask 过滤）
            if (!hit.collider.transform.IsChildOf(transform.root))
            {
                if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
                {
                    damageable.ApplyDamage(_config.damage, 0);
                }
                SelfRecycle();
                return;
            }
        }

        transform.position += _direction * moveDistance;

        _lifeTimer -= deltaTime;
        if (_lifeTimer <= 0f) SelfRecycle();
    }

    private void SelfRecycle()
    {
        ModuleService.Instance.UnregisterHandler(this);
        Dispose();
        
        if (NetworkObject != null && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}