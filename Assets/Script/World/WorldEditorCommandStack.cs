using System;
using System.Collections.Generic;

/// <summary>Runtime 世界编辑器的轻量命令栈。每次可撤销操作都只保存所需的前后状态。</summary>
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

    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;

    public void Execute(string label, Action apply, Action revert)
    {
        if (apply == null || revert == null) return;
        apply();
        undo.Push(new Command { Label = label ?? "编辑", Apply = apply, Revert = revert });
        redo.Clear();
    }

    public bool Undo()
    {
        if (undo.Count == 0) return false;
        Command command = undo.Pop();
        command.Revert();
        redo.Push(command);
        return true;
    }

    public bool Redo()
    {
        if (redo.Count == 0) return false;
        Command command = redo.Pop();
        command.Apply();
        undo.Push(command);
        return true;
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }
}
