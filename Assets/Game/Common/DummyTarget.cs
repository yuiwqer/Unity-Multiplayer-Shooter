using UnityEngine;

public class DummyTarget : MonoBehaviour, IDamageable
{
    private float _health = 30f;

    public void ApplyDamage(float amount, int sourceUnitId)
    {
        _health -= amount;
        Debug.Log($"靶子受到 {amount} 点伤害，剩余血量: {_health}");
        
        // 受击变红
        GetComponent<Renderer>().material.color = Color.red;
        
        if (_health <= 0)
        {
            Debug.Log("靶子被摧毁！");
            Destroy(gameObject);
        }
    }
}