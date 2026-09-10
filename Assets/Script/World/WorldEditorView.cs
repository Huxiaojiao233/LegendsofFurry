using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>绑定 S_WorldEditor 场景里已有的 uGUI，不在运行时创建 Canvas。</summary>
public sealed class WorldEditorView : MonoBehaviour
{
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text hoverText;
    [SerializeField] private TMP_Text selectionText;
    [SerializeField] private TMP_Text sourceText;
    [SerializeField] private TMP_Text worldTitleText;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button reloadButton;
    [SerializeField] private Button undoButton;
    [SerializeField] private Button redoButton;
    [SerializeField] private Button duplicateButton;
    [SerializeField] private Button newFiniteButton;
    [SerializeField] private Button newInfiniteButton;
    [SerializeField] private Button validateButton;
    [SerializeField] private Button generateVisibleButton;
    [SerializeField] private Button randomSeedButton;
    [SerializeField] private Button refreshWorldsButton;
    [SerializeField] private Button importWorldButton;
    [SerializeField] private Button deleteWorldButton;
    [SerializeField] private Button applyWorldSettingsButton;
    [SerializeField] private Button applyStageButton;
    [SerializeField] private Button setStartTileButton;
    [SerializeField] private Button terrainToolButton;
    [SerializeField] private Button heightUpToolButton;
    [SerializeField] private Button heightDownToolButton;
    [SerializeField] private Button objectToolButton;
    [SerializeField] private Button unitToolButton;
    [SerializeField] private Button eraseToolButton;
    [SerializeField] private Button eraseDecorationToolButton;
    [SerializeField] private Button eraseUnitToolButton;
    [SerializeField] private Button selectToolButton;
    [SerializeField] private Button[] terrainPaletteButtons;
    [SerializeField] private Button[] decorationPaletteButtons;
    [SerializeField] private Button[] unitPaletteButtons;
    [SerializeField] private TMP_Dropdown worldList;
    [SerializeField] private TMP_Dropdown decorationList;
    [SerializeField] private TMP_Dropdown unitList;
    [SerializeField] private TMP_Dropdown stageTypeList;
    [SerializeField] private TMP_InputField newWorldIdInput;
    [SerializeField] private TMP_InputField newWorldNameInput;
    [SerializeField] private TMP_InputField chunksXInput;
    [SerializeField] private TMP_InputField chunksYInput;
    [SerializeField] private TMP_InputField chunkSizeInput;
    [SerializeField] private TMP_InputField seedInput;
    [SerializeField] private TMP_InputField heightFrequencyInput;
    [SerializeField] private TMP_InputField decorationDensityInput;
    [SerializeField] private TMP_InputField enemyDensityInput;
    [SerializeField] private TMP_InputField importPathInput;
    [SerializeField] private TMP_InputField worldNameInput;
    [SerializeField] private TMP_InputField stageIdInput;
    [SerializeField] private TMP_InputField stageNameInput;
    [SerializeField] private TMP_InputField stageRewardInput;
    [SerializeField] private TMP_InputField stageRequiredKeyInput;
    [SerializeField] private TMP_InputField stageDropKeyInput;
    [SerializeField] private TMP_Text stageTitleText;
    [SerializeField] private Toggle contourToggle;
    [SerializeField] private Toggle chunkBoundsToggle;

    public Button SaveButton => saveButton;
    public Button ReloadButton => reloadButton;
    public Button UndoButton => undoButton;
    public Button RedoButton => redoButton;
    public Button DuplicateButton => duplicateButton;
    public Button NewFiniteButton => newFiniteButton;
    public Button NewInfiniteButton => newInfiniteButton;
    public Button ValidateButton => validateButton;
    public Button GenerateVisibleButton => generateVisibleButton;
    public Button RandomSeedButton => randomSeedButton;
    public Button RefreshWorldsButton => refreshWorldsButton;
    public Button ImportWorldButton => importWorldButton;
    public Button DeleteWorldButton => deleteWorldButton;
    public Button ApplyWorldSettingsButton => applyWorldSettingsButton;
    public Button ApplyStageButton => applyStageButton;
    public Button SetStartTileButton => setStartTileButton;
    public Button TerrainToolButton => terrainToolButton;
    public Button HeightUpToolButton => heightUpToolButton;
    public Button HeightDownToolButton => heightDownToolButton;
    public Button ObjectToolButton => objectToolButton;
    public Button UnitToolButton => unitToolButton;
    public Button EraseToolButton => eraseToolButton;
    public Button EraseDecorationToolButton => eraseDecorationToolButton;
    public Button EraseUnitToolButton => eraseUnitToolButton;
    public Button SelectToolButton => selectToolButton;
    public Button[] TerrainPaletteButtons => terrainPaletteButtons;
    public Button[] DecorationPaletteButtons => decorationPaletteButtons;
    public Button[] UnitPaletteButtons => unitPaletteButtons;
    public TMP_Dropdown WorldList => worldList;
    public TMP_Dropdown DecorationList => decorationList;
    public TMP_Dropdown UnitList => unitList;
    public TMP_Dropdown StageTypeList => stageTypeList;
    public TMP_InputField NewWorldIdInput => newWorldIdInput;
    public TMP_InputField NewWorldNameInput => newWorldNameInput;
    public TMP_InputField ChunksXInput => chunksXInput;
    public TMP_InputField ChunksYInput => chunksYInput;
    public TMP_InputField ChunkSizeInput => chunkSizeInput;
    public TMP_InputField SeedInput => seedInput;
    public TMP_InputField HeightFrequencyInput => heightFrequencyInput;
    public TMP_InputField DecorationDensityInput => decorationDensityInput;
    public TMP_InputField EnemyDensityInput => enemyDensityInput;
    public TMP_InputField ImportPathInput => importPathInput;
    public TMP_InputField WorldNameInput => worldNameInput;
    public TMP_InputField StageIdInput => stageIdInput;
    public TMP_InputField StageNameInput => stageNameInput;
    public TMP_InputField StageRewardInput => stageRewardInput;
    public TMP_InputField StageRequiredKeyInput => stageRequiredKeyInput;
    public TMP_InputField StageDropKeyInput => stageDropKeyInput;
    public Toggle ContourToggle => contourToggle;
    public Toggle ChunkBoundsToggle => chunkBoundsToggle;

    public void SetStatus(string value)
    {
        if (statusText != null) statusText.text = value ?? string.Empty;
    }

    public void SetHover(string value)
    {
        if (hoverText != null) hoverText.text = value ?? string.Empty;
    }

    public void SetSelection(string value)
    {
        if (selectionText != null) selectionText.text = value ?? string.Empty;
    }

    public void SetSourceHint(string value)
    {
        if (sourceText != null) sourceText.text = value ?? string.Empty;
    }

    public void SetWorldTitle(string value)
    {
        if (worldTitleText != null) worldTitleText.text = value ?? string.Empty;
    }

    public void SetStageTitle(string value)
    {
        if (stageTitleText != null) stageTitleText.text = value ?? string.Empty;
    }
}
