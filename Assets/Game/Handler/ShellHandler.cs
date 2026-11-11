using UnityEngine;

public class ShellHandler : ModuleHandler
{
    private Rigidbody _rb;
    private ShellConfigSO _config;
    private float _lifeTime;

    protected override void OnModuleInitialize()
    {
        _config = Config as ShellConfigSO;
        _rb = GetComponent<Rigidbody>();
    }

    public void Launch(Vector3 throwForce)
    {
        if (_rb == null || _config == null) return;

        // 1. 清理物理状态
        _rb.isKinematic = false; 
        _rb.constraints = RigidbodyConstraints.None;
        _rb.velocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
        _rb.WakeUp();

        // 2. 施加经典冲击力
        _rb.AddForce(throwForce, ForceMode.Impulse);

        // 3. 随机翻滚
        _rb.AddTorque(new Vector3(Random.Range(-15f, 15f), Random.Range(-15f, 15f), Random.Range(-15f, 15f)), ForceMode.Impulse);

        // 4. 重置生命计时
        _lifeTime = _config.lifeTime;
        ModuleService.Instance.RegisterHandler(this);
    }

    public override void OnModuleUpdate(float deltaTime)
    {
        _lifeTime -= deltaTime;
        if (_lifeTime <= 0f)
        {
            // ★ 直接销毁，不用对象池
            ModuleService.Instance.UnregisterHandler(this);
            Destroy(gameObject);
        }
    }
}