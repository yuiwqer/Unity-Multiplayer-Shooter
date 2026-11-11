// ModuleHandler.cs
using System;
using UnityEngine;

public abstract class ModuleHandler : MonoBehaviour, IModuleHandler
{
    protected ModuleConfigSO Config { get; private set; }
    protected string ModuleName => Config != null ? Config.ModuleName : name;
    public bool IsInitialized { get; private set; }
    public bool IsActiveModule => IsInitialized;

    public void Initialize(ModuleConfigSO config)
    {
        if (IsInitialized) return;
        if (config == null) { Debug.LogError($"[{GetType().Name}] Initialize 传入的 config 为 null。", this); return; }

        Config = config;
        IsInitialized = true;
        try { OnModuleInitialize(); }
        catch (Exception e) { Debug.LogException(e, this); }
    }

    public void Dispose()
    {
        if (!IsInitialized) return;
        try { OnModuleDispose(); }
        catch (Exception e) { Debug.LogException(e, this); }
        finally { IsInitialized = false; Config = null; }
    }

    protected virtual void OnModuleInitialize() { }
    public virtual void OnModuleUpdate(float deltaTime) { }
    protected virtual void OnModuleDispose() { }

    protected void Subscribe<T>(Action<T> handler) => EventService.Instance?.Subscribe(handler);
    protected void Unsubscribe<T>(Action<T> handler) => EventService.Instance?.Unsubscribe(handler);
}