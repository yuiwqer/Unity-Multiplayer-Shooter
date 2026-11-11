using System;

// 所有的网络消息都必须实现此接口
public interface INetworkMessage
{
    // 将消息序列化为 JSON 或 字节流（这里为了演示，我们用 JSON）
    string Serialize();
    // 从 JSON 反序列化
    void Deserialize(string json);
}

// 示例：玩家移动同步消息
[Serializable]
public class PlayerMoveMessage : INetworkMessage
{
    public int PlayerId;
    public float X;
    public float Y;
    public float Z;

    public string Serialize() => UnityEngine.JsonUtility.ToJson(this);
    public void Deserialize(string json)
    {
        var msg = UnityEngine.JsonUtility.FromJson<PlayerMoveMessage>(json);
        PlayerId = msg.PlayerId;
        X = msg.X;
        Y = msg.Y;
        Z = msg.Z;
    }
}