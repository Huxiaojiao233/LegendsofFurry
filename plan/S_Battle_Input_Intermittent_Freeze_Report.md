# S_Battle 输入偶发失效分析与修改报告

## 问题现象

偶尔进入 `S_Battle` 后，相机的键盘平移、鼠标拖动与部分战斗快捷键同时没有响应。有时输入会自行恢复，有时需要重新操作窗口或重新进入场景。

## 排查结果

### 已排除

- `BoardCameraController` 在场景中处于启用状态，且没有业务代码会将其关闭。
- 相机移动使用 `Time.unscaledDeltaTime`，不受 `Time.timeScale` 暂停影响。
- 项目中不存在统一的战斗输入锁，也没有发现未释放的输入锁计数。
- `S_Battle` 只有一个启用的 `EventSystem`；UI 指针拦截只影响鼠标，不足以解释键盘和鼠标同时失效。
- 现有 Editor 日志中的两次 `S_Battle` 加载均未出现会中断输入 Update 的运行时异常。

### 根因

战斗输入代码全部直接读取 Unity Input System 的 `Keyboard.current` 和 `Mouse.current`。Input System 在编辑器中的默认策略是：键盘和指针必须由 Game View 持有焦点；场景切换、Console/Inspector 获得焦点或编辑器窗口焦点变化时，这两类事件不会再路由到游戏。Unity Input System 1.20 的[官方枚举说明](https://docs.unity3d.com/Packages/com.unity.inputsystem@1.20/api/UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.html)明确描述了该默认行为。

因此，相机与其他快捷键会同时失效；Game View 再次得到焦点或 Input System 完成设备同步后，输入又可能同时恢复。这与问题的偶发性和“有时自己恢复”一致。

此外，原实现没有处理焦点切换后的拖拽状态清理，也没有处理键鼠设备在恢复焦点后仍为 disabled/current 未恢复的边界情况。

## 修改内容

修改 `Assets/Script/Input/BoardCameraController.cs`：

1. 在 `S_Battle` 相机控制器启用期间，仅在 Unity Editor Play Mode 中把设备输入持续路由到 Game View。
2. 控制器禁用或场景退出时恢复用户原来的编辑器输入策略，不永久修改项目设置。
3. 监听键盘、鼠标的新增、重连和重新启用事件。
4. 应用重新获得焦点或移动端从暂停恢复时，检查并重新启用已有键鼠设备，恢复缺失的 current 引用。
5. 失焦、暂停和控制器禁用时立即结束右键/中键拖拽，避免恢复焦点后残留拖动状态。

正式 Player 包不会启用编辑器专用的持续路由策略，避免玩家切到其他应用后游戏仍接收键盘操作；正式包只保留安全的焦点恢复自愈。

## 验证

新增 `Assets/Editor/Tests/BoardCameraInputRecoveryTests.cs`，覆盖：

- 被禁用的当前 Keyboard/Mouse 可以恢复启用并保持为 current 设备。
- 失去焦点会清理右键平移和中键俯仰状态。

验证状态：

- `Assembly-CSharp.csproj` 编译通过：0 个错误。
- `Assembly-CSharp-Editor.csproj` 编译通过：0 个错误。
- 当前已打开的 Unity Editor 已自动导入改动，Tundra 脚本编译成功并完成程序集重载，没有新增编译错误。
- 新增测试源码使用 Unity 的 Mono/C# 编译器及项目实际引用独立编译通过。
- 已尝试在隔离工程中运行 Unity BatchMode EditMode 测试；Unity Licensing Client 连续两次在许可证通道握手阶段超时，测试没有进入执行阶段。这不是项目编译或测试失败。

仍建议在当前已授权 Editor 完成资源刷新后，通过 Test Runner 执行 `BoardCameraInputRecoveryTests`，并反复从菜单、读取存档进入 `S_Battle`，验证切换到 Console/Inspector 后相机与快捷键仍立即可用。

## 修改结论

本次修改不改变战斗阶段、寻路、卡牌、UI 或相机边界逻辑，只修复输入焦点和输入设备生命周期。修复目标是消除进入 `S_Battle` 后键鼠整体偶发失效，以及焦点恢复后设备未同步导致的持续失效。
