using Project.Gameplay;
using UnityEngine;

// Code by Claude Opus
public class GameRulesTester : MonoBehaviour
{
    [ContextMenu("Run Self Test")]
    private void Run()
    {
        var controller = new GameController { Log = Debug.Log };
        controller.RunSelfTest();
    }
}