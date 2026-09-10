using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// 战斗界面按钮入口。结束回合按钮仍绑定 MovePlayer，实际交给 BattleFlow。
/// </summary>
public class BoardUIController : MonoBehaviour
{
    [SerializeField] private BattleFlow battleFlow;

    private void Awake()
    {
        if (battleFlow == null)
        {
            battleFlow = GetComponent<BattleFlow>();
        }

        if (battleFlow == null)
        {
            battleFlow = FindAnyObjectByType<BattleFlow>();
        }

        if (battleFlow == null)
        {
            battleFlow = gameObject.AddComponent<BattleFlow>();
        }
    }

    public void EndRound()
    {
        if (battleFlow == null)
        {
            battleFlow = FindAnyObjectByType<BattleFlow>();
        }

        battleFlow?.RequestEndPlayerTurn();
    }

    public void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            EndRound();
    }
}
