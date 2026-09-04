using LegendsOfFurry.Content.Contracts;

/// <summary>
/// 战争迷雾：已点亮的关卡不再盖云；未点亮和格子外盖云。战斗中整片云海收起。
/// </summary>
public static class WorldCloudRules
{
    public static bool HasCloudOver(StageDefinition current, StageDefinition stage, bool explored, bool combat)
    {
        if (combat) return false;
        if (stage == null) return true;
        if (explored) return false;
        if (current != null && stage.StageId == current.StageId) return false;
        return true;
    }
}
