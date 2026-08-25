using System;
using System.Collections.Generic;

namespace LegendsOfFurry.Content.Runtime
{
/// <summary>
/// 使用稳定字符串 key 保存同一类别的运行时处理器，并拒绝空 key、空处理器和重复注册。
/// </summary>
/// <typeparam name="THandler">处理器接口或委托类型。</typeparam>
public sealed class ContentOperationRegistry<THandler> where THandler : class
{
    private readonly Dictionary<string, THandler> handlers =
        new Dictionary<string, THandler>(StringComparer.Ordinal);

    /// <summary>
    /// 注册一个处理器；重复 key 会立即抛错，避免后注册实现静默覆盖正式规则。
    /// </summary>
    /// <param name="key">内容数据库使用的稳定操作 key。</param>
    /// <param name="handler">负责该 key 的处理器。</param>
    public void Register(string key, THandler handler)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("内容操作 key 不能为空。", nameof(key));
        }
        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }
        if (!handlers.TryAdd(key, handler))
        {
            throw new InvalidOperationException($"内容操作 key 重复注册：{key}");
        }
    }

    /// <summary>
    /// 尝试按序号比较规则取得处理器。
    /// </summary>
    /// <param name="key">需要查询的稳定操作 key。</param>
    /// <param name="handler">找到的处理器。</param>
    /// <returns>存在完全匹配的注册项时返回 true。</returns>
    public bool TryGet(string key, out THandler handler)
    {
        return handlers.TryGetValue(key ?? string.Empty, out handler);
    }

    /// <summary>
    /// 判断指定稳定 key 是否已经注册，供内容包启动预检使用。
    /// </summary>
    /// <param name="key">需要检查的操作 key。</param>
    /// <returns>存在对应处理器时返回 true。</returns>
    public bool Contains(string key)
    {
        return handlers.ContainsKey(key ?? string.Empty);
    }

    /// <summary>
    /// 返回当前注册 key 的只读快照，避免外部代码修改注册表内部状态。
    /// </summary>
    /// <returns>按序号排序的新只读数组。</returns>
    public IReadOnlyList<string> GetRegisteredKeys()
    {
        string[] keys = new string[handlers.Count];
        handlers.Keys.CopyTo(keys, 0);
        Array.Sort(keys, StringComparer.Ordinal);
        return Array.AsReadOnly(keys);
    }
}
}
