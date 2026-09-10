/// <summary>柏林噪声生成世界时的种子和生物群系参数。</summary>
public sealed class WorldNoiseSettings
{
    public int Seed = 1;
    public float HeightFrequency = 0.075f;
    public float MoistureFrequency = 0.11f;
    public float DecorFrequency = 0.2f;
    public float EnemyFrequency = 0.17f;
    public float WaterLevel = 0.34f;
    public float ForestLevel = 0.62f;
    public float DecorDensity = 0.16f;
    public float EnemyDensity = 0.14f;
    public string EnemyUnitId = "slime";
    public string BossUnitId = "taigao";
}
