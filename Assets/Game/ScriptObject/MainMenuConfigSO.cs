// MainMenuConfigSO.cs

using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "MainMenuConfig", menuName = "Game/Configs/MainMenu")]
public class MainMenuConfigSO : ScriptableObject
{
    [SerializeField] private string gameVersion = "0.1.0";
    [SerializeField] private List<string> levelNames = new();
    public string GameVersion => gameVersion;
    public IReadOnlyList<string> LevelNames => levelNames;
}