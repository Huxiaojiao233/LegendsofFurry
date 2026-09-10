#if UNITY_EDITOR
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class BoardCameraInputRecoveryTests
{
    [Test]
    public void RecoveryReEnablesCurrentKeyboardAndMouse()
    {
        Keyboard originalKeyboard = Keyboard.current;
        Mouse originalMouse = Mouse.current;
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        Mouse mouse = InputSystem.AddDevice<Mouse>();
        keyboard.MakeCurrent();
        mouse.MakeCurrent();
        InputSystem.DisableDevice(keyboard);
        InputSystem.DisableDevice(mouse);

        try
        {
            InvokeStatic("RecoverKeyboard");
            InvokeStatic("RecoverMouse");

            Assert.That(keyboard.enabled, Is.True);
            Assert.That(mouse.enabled, Is.True);
            Assert.That(Keyboard.current, Is.SameAs(keyboard));
            Assert.That(Mouse.current, Is.SameAs(mouse));
        }
        finally
        {
            InputSystem.RemoveDevice(mouse);
            InputSystem.RemoveDevice(keyboard);
            if (originalKeyboard != null && originalKeyboard.added) originalKeyboard.MakeCurrent();
            if (originalMouse != null && originalMouse.added) originalMouse.MakeCurrent();
        }
    }

    [Test]
    public void LosingFocusCancelsActiveMouseGestures()
    {
        GameObject host = new GameObject("BoardCameraInputTest", typeof(Camera));
        BoardCameraController controller = host.AddComponent<BoardCameraController>();
        SetField(controller, "rightMousePanning", true);
        SetField(controller, "middleMousePitching", true);

        try
        {
            typeof(BoardCameraController)
                .GetMethod("OnApplicationFocus", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { false });

            Assert.That(GetField<bool>(controller, "rightMousePanning"), Is.False);
            Assert.That(GetField<bool>(controller, "middleMousePitching"), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(host);
        }
    }

    private static void InvokeStatic(string methodName)
    {
        typeof(BoardCameraController)
            .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
    }

    private static void SetField(object target, string fieldName, object value)
    {
        typeof(BoardCameraController)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(target, value);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        return (T)typeof(BoardCameraController)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(target);
    }
}
#endif
