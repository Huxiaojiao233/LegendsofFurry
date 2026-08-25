#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

/// <summary>覆盖阶段零要求的 Unity 进入和退出 PlayMode 冒烟验证。</summary>
public sealed class BattlePlayModeSmokeTests
{
    /// <summary>进入一帧 PlayMode 后安全退出，捕获启动期编译、加载和生命周期异常。</summary>
    [UnityTest]
    public IEnumerator ProjectCanEnterAndExitPlayMode()
    {
        yield return new EnterPlayMode();
        yield return null;
        Assert.That(UnityEngine.Application.isPlaying, Is.True);
        yield return new ExitPlayMode();
    }
}
#endif
