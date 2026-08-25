public enum HeroClass { Warrior, Ranger, Mage, Assassin, Priest }

public static class GameSession
{
    private const string ClassKey = "LegendsOfFurry.SelectedClass";
    private static bool hasRuntimeSelection;
    private static HeroClass selectedClass = HeroClass.Warrior;

    public static HeroClass SelectedClass
    {
        get
        {
            if (!hasRuntimeSelection && UnityEngine.PlayerPrefs.HasKey(ClassKey))
                selectedClass = (HeroClass)UnityEngine.PlayerPrefs.GetInt(ClassKey, 0);
            return selectedClass;
        }
    }

    public static void SelectClass(HeroClass heroClass)
    {
        selectedClass = heroClass;
        hasRuntimeSelection = true;
        UnityEngine.PlayerPrefs.SetInt(ClassKey, (int)heroClass);
        UnityEngine.PlayerPrefs.Save();
    }
    public static void ClearSelectedClass()
    {
        hasRuntimeSelection = false;
        UnityEngine.PlayerPrefs.DeleteKey(ClassKey);
        UnityEngine.PlayerPrefs.Save();
    }
}
