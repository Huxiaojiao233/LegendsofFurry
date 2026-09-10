#if UNITY_EDITOR
using System.Reflection;
using LegendsOfFurry.Content.Runtime;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

/// <summary>HUD 启动不得改场景里已有 Image/Button 的激活状态，也不能往上面加 EventTrigger。</summary>
public sealed class BattleRuntimeHudTests
{
    [Test]
    public void EmptyEquipmentSlotsStayActiveWithoutEventTrigger()
    {
        GameObject canvas = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
        GameObject hudRoot = new GameObject("BattleInterface", typeof(RectTransform));
        hudRoot.transform.SetParent(canvas.transform, false);
        GameObject selfInfo = new GameObject("P_SelfInfo", typeof(RectTransform));
        selfInfo.transform.SetParent(hudRoot.transform, false);
        GameObject details = new GameObject("SelfDetails", typeof(RectTransform));
        details.transform.SetParent(selfInfo.transform, false);

        string[] names = { "Weapon", "Armor1", "Accessories", "Armor2", "Treasure", "Boot" };
        GameObject[] slots = new GameObject[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            slots[i] = new GameObject(
                names[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            slots[i].transform.SetParent(details.transform, false);
            slots[i].transform.localScale = Vector3.one;
        }

        BattleRuntimeHud hud = hudRoot.AddComponent<BattleRuntimeHud>();
        typeof(BattleRuntimeHud)
            .GetMethod("ResolveEquipmentBindings", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(hud, null);

        try
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Assert.That(slots[i].activeSelf, Is.True, names[i] + " 必须保持激活");
                Assert.That(slots[i].GetComponent<EventTrigger>(), Is.Null, names[i] + " 不得加 EventTrigger");
                Assert.That(slots[i].transform.localScale, Is.EqualTo(Vector3.zero));
            }
        }
        finally
        {
            Object.DestroyImmediate(canvas);
        }
    }

    [Test]
    public void CurrentCardControlPilesBindWithoutIncompleteError()
    {
        GameObject hudRoot = new GameObject("BattleInterface", typeof(RectTransform));
        CreateTmpPath(hudRoot.transform, "P_SelfInfo", "SelfDetails", "T_Self_Name");
        CreateTmpPath(hudRoot.transform, "P_SelfInfo", "SelfDetails", "P_Carrer");
        CreateTmpPath(hudRoot.transform, "P_SelfInfo", "S_StatusColumn", "Details");
        CreateTmpPath(hudRoot.transform, "I_RoundInfo", "RoundObj", "Title");
        CreateTmpPath(hudRoot.transform, "I_RoundInfo", "RoundNumber", "Title");
        CreateTmpPath(hudRoot.transform, "C_CardControl", "Stack", "Number");
        CreateTmpPath(hudRoot.transform, "C_CardControl", "Stack", "N_Fold");
        BattleRuntimeHud hud = hudRoot.AddComponent<BattleRuntimeHud>();

        try
        {
            typeof(BattleRuntimeHud)
                .GetMethod("ResolveInterfaceBindings", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hud, null);
            LogAssert.NoUnexpectedReceived();
        }
        finally
        {
            Object.DestroyImmediate(hudRoot);
        }
    }

    [Test]
    public void LoadPortraitSpriteNullDefinitionReturnsNull()
    {
        Assert.That(TokenVisualRuntime.LoadPortraitSprite(null), Is.Null);
    }

    [Test]
    public void HidingAuthoredCombatUiDoesNotDeactivateOrAddCanvasGroup()
    {
        GameObject endRound = new GameObject(
            "B_EndRound", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        endRound.transform.localScale = Vector3.one;
        MethodInfo hide = typeof(WorldPlaySession).GetMethod(
            "SetAuthoredUiVisible", BindingFlags.Static | BindingFlags.NonPublic);
        object[] args = { endRound, false, Vector3.one, false };
        hide.Invoke(null, args);

        try
        {
            Assert.That(endRound.activeSelf, Is.True);
            Assert.That(endRound.GetComponent<CanvasGroup>(), Is.Null);
            Assert.That(endRound.transform.localScale, Is.EqualTo(Vector3.zero));
        }
        finally
        {
            Object.DestroyImmediate(endRound);
        }
    }

    private static void CreateTmpPath(Transform root, params string[] path)
    {
        Transform current = root;
        for (int i = 0; i < path.Length; i++)
        {
            Transform child = current.Find(path[i]);
            if (child == null)
            {
                GameObject go = new GameObject(path[i], typeof(RectTransform));
                go.transform.SetParent(current, false);
                child = go.transform;
            }

            current = child;
        }

        if (current.GetComponent<TextMeshProUGUI>() == null)
            current.gameObject.AddComponent<TextMeshProUGUI>();
    }
}
#endif
