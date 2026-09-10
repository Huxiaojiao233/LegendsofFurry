using System;
using System.Collections.Generic;

/// <summary>Runtime 世界编辑器命令栈。拖拽笔划合并成一条撤销记录。</summary>
public sealed class WorldEditorCommandStack
{
    private sealed class Command
    {
        public string Label;
        public Action Apply;
        public Action Revert;
    }

    private readonly Stack<Command> undo = new Stack<Command>();
    private readonly Stack<Command> redo = new Stack<Command>();
    private readonly List<Action> strokeApply = new List<Action>();
    private readonly List<Action> strokeRevert = new List<Action>();
    private string strokeLabel;
    private bool stroking;

    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    public bool IsStroking => stroking;
    public bool IsDirty { get; private set; }
    public event Action Changed;

    public void Execute(string label, Action apply, Action revert)
    {
        if (apply == null || revert == null) return;
        if (stroking)
        {
            apply();
            strokeApply.Add(apply);
            strokeRevert.Add(revert);
            MarkDirty();
            return;
        }

        apply();
        undo.Push(new Command { Label = label ?? "编辑", Apply = apply, Revert = revert });
        redo.Clear();
        MarkDirty();
        Changed?.Invoke();
    }

    public void BeginStroke(string label)
    {
        if (stroking) EndStroke();
        stroking = true;
        strokeLabel = string.IsNullOrWhiteSpace(label) ? "绘制" : label;
        strokeApply.Clear();
        strokeRevert.Clear();
    }

    public void EndStroke()
    {
        if (!stroking) return;
        stroking = false;
        if (strokeApply.Count == 0)
        {
            strokeRevert.Clear();
            return;
        }

        Action[] applies = strokeApply.ToArray();
        Action[] reverts = strokeRevert.ToArray();
        strokeApply.Clear();
        strokeRevert.Clear();
        undo.Push(new Command
        {
            Label = strokeLabel,
            Apply = () =>
            {
                for (int i = 0; i < applies.Length; i++)
                    applies[i]();
            },
            Revert = () =>
            {
                for (int i = reverts.Length - 1; i >= 0; i--)
                    reverts[i]();
            }
        });
        redo.Clear();
        MarkDirty();
        Changed?.Invoke();
    }

    public bool Undo()
    {
        if (stroking) EndStroke();
        if (undo.Count == 0) return false;
        Command command = undo.Pop();
        command.Revert();
        redo.Push(command);
        MarkDirty();
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        if (stroking) EndStroke();
        if (redo.Count == 0) return false;
        Command command = redo.Pop();
        command.Apply();
        undo.Push(command);
        MarkDirty();
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        if (stroking) EndStroke();
        undo.Clear();
        redo.Clear();
        strokeApply.Clear();
        strokeRevert.Clear();
        IsDirty = false;
        Changed?.Invoke();
    }

    public void MarkSaved()
    {
        IsDirty = false;
        Changed?.Invoke();
    }

    private void MarkDirty() => IsDirty = true;
}
