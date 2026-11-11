// IModuleHandler.cs
public interface IModuleHandler
{
    bool IsActiveModule { get; }
    void Initialize(ModuleConfigSO config);
    void Dispose();
    void OnModuleUpdate(float deltaTime);
}