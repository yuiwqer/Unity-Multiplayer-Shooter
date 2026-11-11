using System;
using Game.Events;
using Unity.Netcode;
using UnityEngine;

public class WeaponHandler : NetworkModuleHandler, ISaveable
{
    private WeaponConfigSO _config;
    private float _cooldown;
    private int _currentAmmo;
    private bool _isReloading;
    private float _reloadTimer;

    public string SaveKey => ModuleName;

    #region 生命周期
    protected override void OnModuleInitialize()
    {
        _config = Config as WeaponConfigSO;
        if (_config == null)
        {
            Debug.LogError("[WeaponHandler] 配置类型不匹配！");
            return;
        }

        _currentAmmo = _config.magazineSize;
        _isReloading = false;

        if (IsServer) 
        {
            SaveService.Instance?.RestoreSaveable(this);
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        // 网络生成后，如果是自己，立刻向 UI 推送一次初始弹药数量
        if (IsOwner && _config != null)
        {
            EventService.Instance.Publish(new AmmoChangedEvent(_currentAmmo, _config.magazineSize));
        }
    }

    public override void OnModuleUpdate(float deltaTime)
    {
        if (_config == null || !IsSpawned) return;

        // 服务器端处理换弹计时
        if (IsServer && _isReloading)
        {
            _reloadTimer -= deltaTime;
            if (_reloadTimer <= 0f)
            {
                FinishReloadServer();
            }
        }

        // 客户端（拥有者）处理输入
        if (!IsOwner) return;
        if (_isReloading) return;

        _cooldown -= deltaTime;
        
        // 开火
        if (_cooldown <= 0f && InputService.Instance.GetButton(InputService.Action.Fire))
        {
            CalculateAndSendFire();
            _cooldown = 1f / _config.fireRate;
        }

        // 换弹
        if (InputService.Instance.GetButtonDown(InputService.Action.Reload))
        {
            StartReloadServerRpc();
        }
    }
    #endregion

    #region 开火逻辑
    private void CalculateAndSendFire()
    {
        if (_currentAmmo <= 0) return;

        // 1. 获取相机准星射线
        Ray aimRay = CameraService.Instance.GetCenterScreenRay();
        
        // 2. 默认目标点为前方 100 米
        Vector3 targetPoint = aimRay.origin + aimRay.direction * 100f;

        // 3. 射线检测获取真实碰撞点（过滤掉玩家自己）
        RaycastHit[] hits = Physics.RaycastAll(aimRay, 100f);
        foreach (var hit in hits)
        {
            if (!hit.collider.transform.IsChildOf(transform.root))
            {
                targetPoint = hit.point;
                break;
            }
        }

        // 4. 计算枪口位置（朝武器正前方偏移 1 米）
        Vector3 spawnPos = transform.position + transform.forward * 1.0f;

        // 5. 真实飞行方向 = 目标点 - 枪口
        Vector3 fireDirection = (targetPoint - spawnPos).normalized;

        // 6. 发送给服务器
        FireServerRpc(spawnPos, fireDirection);
    }

    [ServerRpc]
    private void FireServerRpc(Vector3 spawnPos, Vector3 fireDirection)
    {
        if (_currentAmmo <= 0 || _isReloading) return;
        
        _currentAmmo--;
        SyncAmmoClientRpc(_currentAmmo);

        // 服务器使用客户端算好的参数生成子弹
        var handler = ModuleService.Instance.SpawnHandler("basic_bullet", spawnPos, Quaternion.LookRotation(fireDirection));
        if (handler is ProjectileHandler projectile)
        {
            projectile.Launch(fireDirection);
        }

        // ★ 广播开火表现给所有人
        FireEffectClientRpc();
    }

    [ClientRpc]
    private void FireEffectClientRpc()
    {
        // 只有开火的自己才有震屏
        if (IsOwner)
        {
            CameraService.Instance.Shake(0.1f, 0.05f);
        }

        // ★ 所有人（包括自己和其他玩家）都会看到抛出的小弹壳
        SpawnDebris(_config.shellConfig);
    }
    #endregion

    #region 换弹逻辑
    [ServerRpc]
    private void StartReloadServerRpc()
    {
        if (_currentAmmo >= _config.magazineSize || _isReloading) return;

        _isReloading = true;
        _reloadTimer = _config.reloadTime;
        
        StartReloadClientRpc();
    }

    private void FinishReloadServer()
    {
        _isReloading = false;
        _currentAmmo = _config.magazineSize;
        
        SyncAmmoClientRpc(_currentAmmo);
        FinishReloadClientRpc();
    }

    [ClientRpc]
    private void StartReloadClientRpc()
    {
        if (IsOwner)
        {
            _isReloading = true; 
            _reloadTimer = _config.reloadTime;
        }

        // ★ 所有人都会看到掉落的大弹夹
        SpawnDebris(_config.magazineConfig);
    }

    [ClientRpc]
    private void FinishReloadClientRpc()
    {
        if (IsOwner)
        {
            _isReloading = false;
        }
    }

    [ClientRpc]
    private void SyncAmmoClientRpc(int newAmmo)
    {
        _currentAmmo = newAmmo; 
        if (IsOwner)
        {
            EventService.Instance.Publish(new AmmoChangedEvent(_currentAmmo, _config.magazineSize));
        }
    }
    #endregion

    #region 抛射物表现（弹壳/弹夹）
    /// <summary>
    /// 纯本地生成表现物，不涉及网络同步。所有人都在本地各自计算物理。
    /// </summary>
    private void SpawnDebris(ShellConfigSO config)
    {
        if (config == null || config.shellPrefab == null) return;

        var debrisGo = Instantiate(config.shellPrefab);
        if (debrisGo != null && debrisGo.TryGetComponent<ShellHandler>(out var shell))
        {
            // 生成位置：武器右侧偏上，防止卡住玩家自己
            Vector3 spawnPos = transform.position + transform.right * 0.8f + transform.up * 0.2f + transform.forward * 0.3f;
            shell.transform.position = spawnPos;

            // 忽略与玩家自身碰撞体的物理碰撞，防止把玩家顶飞
            var shellCollider = debrisGo.GetComponent<Collider>();
            var playerColliders = GetComponentsInParent<Collider>();
            if (shellCollider != null)
            {
                foreach (var pCol in playerColliders)
                {
                    if (pCol != null) Physics.IgnoreCollision(shellCollider, pCol, true);
                }
            }

            // 根据 SO 计算抛射力
            float randomUp = config.upForce * UnityEngine.Random.Range(0.9f, 1.1f);
            float randomRight = config.rightForce * UnityEngine.Random.Range(0.9f, 1.1f);
            Vector3 throwForce = (transform.right * randomRight) + (transform.up * randomUp) + (transform.forward * config.forwardForce);

            shell.Initialize(config); 
            shell.Launch(throwForce); 
        }
    }
    #endregion

    #region 存档与清理
    protected override void OnModuleDispose()
    {
        if (IsServer)
        {
            SaveService.Instance?.RegisterSaveable(this);
        }
    }

    public string SerializeState()
    {
        var state = new WeaponSaveState { currentAmmo = _currentAmmo };
        return JsonUtility.ToJson(state);
    }

    public void DeserializeState(string json)
    {
        var state = JsonUtility.FromJson<WeaponSaveState>(json);
        _currentAmmo = state.currentAmmo;
    }
    #endregion
}

[Serializable]
public class WeaponSaveState
{
    public int currentAmmo;
}