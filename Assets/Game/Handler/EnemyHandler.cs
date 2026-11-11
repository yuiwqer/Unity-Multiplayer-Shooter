using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class EnemyHandler : NetworkModuleHandler, IDamageable
{
    private EnemyConfigSO _config;
    private Renderer _renderer;
    private Color _originalColor;
    private Coroutine _flashCoroutine;

    // NGO 网络变量：同步血量
    private NetworkVariable<float> _currentHealth = new NetworkVariable<float>(
        0f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    protected override void OnModuleInitialize()
    {
        _config = Config as EnemyConfigSO;
        _renderer = GetComponentInChildren<Renderer>();
        if (_renderer != null) _originalColor = _renderer.material.color;

        if (IsServer)
        {
            _currentHealth.Value = _config.maxHealth;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _currentHealth.OnValueChanged += OnHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        _currentHealth.OnValueChanged -= OnHealthChanged;
        base.OnNetworkDespawn();
    }

    private void OnHealthChanged(float previousValue, float newValue)
    {
        if (newValue < previousValue && newValue > 0)
        {
            if (_flashCoroutine != null) StopCoroutine(_flashCoroutine);
            _flashCoroutine = StartCoroutine(FlashRoutine());
        }
    }

    public void ApplyDamage(float amount, int sourceUnitId)
    {
        if (!IsServer) return; 
        
        _currentHealth.Value -= amount;
        Debug.Log($"[敌人] 受到 {amount} 伤害，剩余血量: {_currentHealth.Value}");

        if (_currentHealth.Value <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        if (_config.deathEffectPrefab != null)
        {
            var fx = Instantiate(_config.deathEffectPrefab, transform.position, Quaternion.identity);
            Destroy(fx, 2f); 
        }

        DieClientRpc();

        // ★ 核心修复：由于对象马上要销毁，把延迟生成的协程托管给常驻的 ModuleService
        if (_config.respawnOnDeath && IsServer)
        {
            ModuleService.Instance.StartCoroutine(DelayedRespawnRoutine(ModuleName, transform.position, _config));
        }

        ModuleService.Instance.UnregisterHandler(this);
        Dispose();
        NetworkObject.Despawn(true);
    }

    [ClientRpc]
    private void DieClientRpc()
    {
        // 播放死亡音效等
    }

    // ★ 托管给 ModuleService 的协程
    private IEnumerator DelayedRespawnRoutine(string moduleName, Vector3 deathPos, EnemyConfigSO config)
    {
        yield return new WaitForSeconds(config.respawnDelay);

        if (NetworkService.Instance != null && NetworkService.Instance.IsServer)
        {
            Vector3 randomOffset = new Vector3(
                Random.Range(-config.respawnRadius, config.respawnRadius),
                0f,
                Random.Range(-config.respawnRadius, config.respawnRadius)
            );
            Vector3 spawnPos = deathPos + randomOffset;

            ModuleService.Instance.SpawnHandler(moduleName, spawnPos, Quaternion.identity);
            Debug.Log($"[敌人] 在 {spawnPos} 重生了新靶子！");
        }
    }

    private IEnumerator FlashRoutine()
    {
        if (_renderer == null) yield break;
        _renderer.material.color = _config.hitFlashColor;
        yield return new WaitForSeconds(_config.hitFlashDuration);
        _renderer.material.color = _originalColor;
    }
}