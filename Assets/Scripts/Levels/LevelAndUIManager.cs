using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[Serializable]
public class LevelScenario
{
    [Tooltip("Judul skenario level yang ditampilkan di bagian atas HUD")]
    public string levelTitle;

    [TextArea(2, 4)]
    [Tooltip("Deskripsi misi / permasalahan logika yang harus diselesaikan siswa")]
    public string missionDescription;

    [Tooltip("Batas maksimal jumlah kartu gerbang logika (AND, OR, NOT, XOR, NAND) untuk efisiensi rangkaian")]
    public int maxGateCardsQuota = 1;

    [Tooltip("Target Tabel Kebenaran 4 baris berurutan untuk kombinasi input AB: [0]=00, [1]=01, [2]=10, [3]=11")]
    public bool[] expectedTruthTable = new bool[4];
}

public class LevelAndUIManager : MonoBehaviour
{
    private const int TruthTableRowCount = 4;

    [Header("1. Referensi Sistem & Node Inti")]
    [Tooltip("Referensi ke objek CircuitManager di Scene")]
    public CircuitManager circuitManager;

    [Tooltip("Kartu Input Source A (Card_Input_A)")]
    public GateNode inputANode;

    [Tooltip("Kartu Input Source B (Card_Input_B)")]
    public GateNode inputBNode;

    [Tooltip("Kartu Output Actuator (Card_Output)")]
    public GateNode outputNode;

    [Header("2. Referensi Komponen UI (TextMeshPro & Tombol)")]
    [Tooltip("Teks UI untuk menampilkan judul level saat ini")]
    public TMP_Text txtLevelTitle;

    [Tooltip("Teks UI untuk menampilkan deskripsi misi/soal")]
    public TMP_Text txtMissionDesc;

    [Tooltip("Teks UI untuk menampilkan pemakaian kuota kartu gerbang secara real-time")]
    public TMP_Text txtQuotaStatus;

    [Tooltip("Teks UI untuk menampilkan umpan balik status rangkaian & hasil verifikasi")]
    public TMP_Text txtFeedback;

    [Tooltip("Objek tombol 'Level Berikutnya' / 'Mode Sandbox Bebas' (di-auto-bind ke Btn_NextLevel jika kosong)")]
    public GameObject nextLevelButtonObj;

    [Tooltip("Teks label pada tombol nextLevelButtonObj (otomatis di-auto-bind ke TMP_Text child jika kosong)")]
    public TMP_Text txtNextLevelButtonLabel;

    [Tooltip("Objek tombol 'Reset Level' / 'Main Ulang (Level 1)' (di-auto-bind ke Btn_ResetLevel jika kosong)")]
    public GameObject resetLevelButtonObj;

    [Tooltip("Teks label pada tombol resetLevelButtonObj (otomatis di-auto-bind ke TMP_Text child jika kosong)")]
    public TMP_Text txtResetLevelButtonLabel;

    [Tooltip("Teks pada tombol Toggle Input A (opsional, diperbarui real-time menjadi 'Input A: 0/1')")]
    public TMP_Text txtBtnToggleA;

    [Tooltip("Teks pada tombol Toggle Input B (opsional, diperbarui real-time menjadi 'Input B: 0/1')")]
    public TMP_Text txtBtnToggleB;

    [Header("3. Daftar Skenario Level & Status Aktif")]
    [Tooltip("Daftar 5 skenario level berjenjang Kurikulum Merdeka (otomatis terisi di Awake jika kosong)")]
    public List<LevelScenario> levels = new List<LevelScenario>();

    [Tooltip("Indeks level yang sedang aktif (0..4 = Level 1..5)")]
    public int currentLevelIndex = 0;

    [Header("4. Panel Matrix & Rumus Boolean Real-Time (Hari 15)")]
    [Tooltip("Panel UI prosedural Live Truth Table Scanner (otomatis dibangun di Canvas bila kosong)")]
    public GameObject truthTableScannerPanelObj;

    private bool isVerifying = false;
    private bool isSandboxMode = false;
    private bool awaitingPostVictoryChoice = false;
    private bool[] levelCompletedFlags;

    // Komponen Matrix UI Prosedural
    private TMP_Text txtBooleanFormula;
    private TMP_Text txtCurrentOutputReadout;
    private readonly Image[] matrixRowBackgrounds = new Image[TruthTableRowCount];
    private readonly TMP_Text[] matrixRowTargetTexts = new TMP_Text[TruthTableRowCount];
    private readonly TMP_Text[] matrixRowMeasuredTexts = new TMP_Text[TruthTableRowCount];
    private readonly TMP_Text[] matrixRowStatusTexts = new TMP_Text[TruthTableRowCount];
    private readonly bool[] matrixRowScannedFlags = new bool[TruthTableRowCount];
    private readonly bool[] matrixRowPassedFlags = new bool[TruthTableRowCount];

    private int scanningRowIndex = -1;
    private string lastTopologySignature = string.Empty;
    private bool hasLastTopologySignature = false;

    private static readonly Color MatrixPanelBgColor = new Color(0.04f, 0.08f, 0.15f, 0.80f);
    private static readonly Color MatrixAccentCyanColor = new Color(0.18f, 0.88f, 1.00f, 0.95f);
    private static readonly Color MatrixHeaderBgColor = new Color(0.08f, 0.16f, 0.28f, 0.90f);
    private static readonly Color RowIdleBgColor = new Color(0.07f, 0.12f, 0.20f, 0.65f);
    private static readonly Color RowActiveManualBgColor = new Color(0.12f, 0.42f, 0.65f, 0.78f);
    private static readonly Color RowScanningBgColor = new Color(0.55f, 0.42f, 0.08f, 0.85f);
    private static readonly Color RowPassBgColor = new Color(0.08f, 0.34f, 0.18f, 0.75f);
    private static readonly Color RowFailBgColor = new Color(0.42f, 0.10f, 0.12f, 0.78f);
    private static readonly Color RowSandboxDoneBgColor = new Color(0.08f, 0.30f, 0.36f, 0.75f);
    private static readonly Color MatrixBrightGreenColor = new Color(0.25f, 1.00f, 0.35f, 1f);
    private static readonly Color MatrixBrightRedColor = new Color(1.00f, 0.30f, 0.25f, 1f);
    private static readonly Color MatrixBrightCyanColor = new Color(0.30f, 0.95f, 1.00f, 1f);

    public bool IsSandboxMode => isSandboxMode;

    private void Awake()
    {
        // Isi otomatis 5 skenario default jika daftar levels di Inspector masih kosong
        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }
        else
        {
            EnsureLevelCompletedFlagsInitialized();
        }

        // Cari otomatis CircuitManager jika belum di-assign di Inspector
        if (circuitManager == null)
        {
            circuitManager = FindAnyObjectByType<CircuitManager>();
        }

        AutoBindCoreNodesIfNeeded();
        AutoBindToggleButtonsTextIfNeeded();
        AutoBindActionButtonsIfNeeded();
        InitializeTruthTableMatrixUI();
    }

    private void Start()
    {
        LoadLevel(currentLevelIndex);
    }

    private void Update()
    {
        UpdateQuotaStatusUI();
        UpdateInputToggleButtonsUI();
        UpdateTruthTableMatrixLive();
    }

    /// <summary>
    /// Mencari referensi Input A, Input B, dan Output secara otomatis jika belum diisi manual di Inspector.
    /// </summary>
    private void AutoBindCoreNodesIfNeeded()
    {
        if (inputANode != null && inputBNode != null && outputNode != null)
        {
            return;
        }

#if UNITY_6000_5_OR_NEWER
        GateNode[] nodes = FindObjectsByType<GateNode>();
#else
        GateNode[] nodes = FindObjectsByType<GateNode>(FindObjectsSortMode.None);
#endif
        for (int i = 0; i < nodes.Length; i++)
        {
            GateNode node = nodes[i];
            if (node == null)
            {
                continue;
            }

            if (outputNode == null && node.nodeType == NodeType.OutputActuator)
            {
                outputNode = node;
            }
            else if (node.nodeType == NodeType.InputSource)
            {
                string objName = node.gameObject.name.ToUpperInvariant();
                if (inputANode == null && objName.Contains("A"))
                {
                    inputANode = node;
                }
                else if (inputBNode == null && objName.Contains("B"))
                {
                    inputBNode = node;
                }
            }
        }
    }

    /// <summary>
    /// Mencari otomatis TMP_Text pada tombol Btn_ToggleInputA dan Btn_ToggleInputB jika belum di-assign di Inspector.
    /// </summary>
    private void AutoBindToggleButtonsTextIfNeeded()
    {
        if (txtBtnToggleA == null)
        {
            GameObject btnA = GameObject.Find("Btn_ToggleInputA");
            if (btnA != null)
            {
                txtBtnToggleA = btnA.GetComponentInChildren<TMP_Text>(true);
            }
        }

        if (txtBtnToggleB == null)
        {
            GameObject btnB = GameObject.Find("Btn_ToggleInputB");
            if (btnB != null)
            {
                txtBtnToggleB = btnB.GetComponentInChildren<TMP_Text>(true);
            }
        }
    }

    /// <summary>
    /// Mencari otomatis referensi tombol & label untuk Next Level dan Reset Level jika belum diisi di Inspector.
    /// </summary>
    private void AutoBindActionButtonsIfNeeded()
    {
        if (nextLevelButtonObj == null)
        {
            nextLevelButtonObj = GameObject.Find("Btn_NextLevel");
        }

        if (txtNextLevelButtonLabel == null && nextLevelButtonObj != null)
        {
            txtNextLevelButtonLabel = nextLevelButtonObj.GetComponentInChildren<TMP_Text>(true);
        }

        if (resetLevelButtonObj == null)
        {
            resetLevelButtonObj = GameObject.Find("Btn_ResetLevel");
        }

        if (txtResetLevelButtonLabel == null && resetLevelButtonObj != null)
        {
            txtResetLevelButtonLabel = resetLevelButtonObj.GetComponentInChildren<TMP_Text>(true);
        }
    }

    /// <summary>
    /// Membangun secara prosedural panel samping "Panel_TruthTableScanner" di Canvas (1920x1080)
    /// agar berada rapi di sisi kiri tanpa menabrak Panel_TopHud, Panel_BottomHUD, maupun area tengah kamera AR.
    /// </summary>
    private void InitializeTruthTableMatrixUI()
    {
        if (txtBooleanFormula != null)
        {
            return;
        }

        GameObject existingPanel = truthTableScannerPanelObj != null
            ? truthTableScannerPanelObj
            : GameObject.Find("Panel_TruthTableScanner");

        if (existingPanel != null && existingPanel != truthTableScannerPanelObj)
        {
            truthTableScannerPanelObj = existingPanel;
        }

        Canvas targetCanvas = FindTargetCanvas();
        if (targetCanvas == null)
        {
            return;
        }

        TMP_FontAsset safeFont = ResolveSafeTMPFont();

        if (truthTableScannerPanelObj == null)
        {
            truthTableScannerPanelObj = new GameObject("Panel_TruthTableScanner", typeof(RectTransform), typeof(Image));
            truthTableScannerPanelObj.transform.SetParent(targetCanvas.transform, false);
        }

        RectTransform panelRect = truthTableScannerPanelObj.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.anchoredPosition = new Vector2(22f, -176f);
        panelRect.sizeDelta = new Vector2(460f, 344f);

        Image panelImage = truthTableScannerPanelObj.GetComponent<Image>();
        if (panelImage != null)
        {
            panelImage.color = MatrixPanelBgColor;
            panelImage.raycastTarget = false;
        }

        // Garis aksen sian di tepi atas panel
        CreateMatrixPanelAccentBar(panelRect);

        // Header & Rumus Aljabar Boolean Aktif
        CreateMatrixText("Txt_ScannerBadge", panelRect, new Vector2(14f, -10f), new Vector2(432f, 22f),
            "MATRIX TABEL KEBENARAN & RUMUS", 13f, TextAlignmentOptions.Left, MatrixAccentCyanColor, safeFont);

        txtBooleanFormula = CreateMatrixText("Txt_BooleanFormula", panelRect, new Vector2(14f, -34f), new Vector2(432f, 38f),
            "Rumus Rakitan: Y = (Belum Terhubung)", 17f, TextAlignmentOptions.Left, Color.white, safeFont);
        txtBooleanFormula.enableAutoSizing = true;
        txtBooleanFormula.fontSizeMin = 12f;
        txtBooleanFormula.fontSizeMax = 18f;

        txtCurrentOutputReadout = CreateMatrixText("Txt_CurrentOutputReadout", panelRect, new Vector2(14f, -74f), new Vector2(432f, 24f),
            "Output Y = - [BELUM TERLACAK]", 15f, TextAlignmentOptions.Left, new Color(1f, 0.72f, 0.25f, 1f), safeFont);

        // Garis pemisah horizontal
        GameObject dividerObj = new GameObject("Divider_Scanner", typeof(RectTransform), typeof(Image));
        dividerObj.transform.SetParent(panelRect, false);
        RectTransform divRect = dividerObj.GetComponent<RectTransform>();
        divRect.anchorMin = new Vector2(0f, 1f);
        divRect.anchorMax = new Vector2(0f, 1f);
        divRect.pivot = new Vector2(0f, 1f);
        divRect.anchoredPosition = new Vector2(14f, -102f);
        divRect.sizeDelta = new Vector2(432f, 2f);
        Image divImg = dividerObj.GetComponent<Image>();
        divImg.color = new Color(0.22f, 0.65f, 0.85f, 0.45f);
        divImg.raycastTarget = false;

        // Baris header kolom: [A] [B] [Target] [Uji Y] [Status]
        RectTransform headerRowRect = CreateMatrixRowContainer("Row_Header", panelRect, -110f, 32f, MatrixHeaderBgColor);
        Color headerTextColor = new Color(0.75f, 0.92f, 1f, 1f);
        CreateMatrixColumnText("Hdr_A", headerRowRect, 0f, 46f, 32f, "[A]", 14f, headerTextColor, safeFont);
        CreateMatrixColumnText("Hdr_B", headerRowRect, 46f, 46f, 32f, "[B]", 14f, headerTextColor, safeFont);
        CreateMatrixColumnText("Hdr_Target", headerRowRect, 92f, 92f, 32f, "[Target]", 14f, headerTextColor, safeFont);
        CreateMatrixColumnText("Hdr_Measured", headerRowRect, 184f, 86f, 32f, "[Uji Y]", 14f, headerTextColor, safeFont);
        CreateMatrixColumnText("Hdr_Status", headerRowRect, 270f, 162f, 32f, "[Status]", 14f, headerTextColor, safeFont);

        // 4 Baris data kombinasi input AB: 00, 01, 10, 11
        for (int i = 0; i < TruthTableRowCount; i++)
        {
            float rowY = -146f - (i * 48f);
            RectTransform rowRect = CreateMatrixRowContainer($"Row_Data_{i}", panelRect, rowY, 44f, RowIdleBgColor);
            matrixRowBackgrounds[i] = rowRect.GetComponent<Image>();

            string bitA = (i == 2 || i == 3) ? "1" : "0";
            string bitB = (i == 1 || i == 3) ? "1" : "0";

            CreateMatrixColumnText($"Col_A_{i}", rowRect, 0f, 46f, 44f, bitA, 16f, Color.white, safeFont);
            CreateMatrixColumnText($"Col_B_{i}", rowRect, 46f, 46f, 44f, bitB, 16f, Color.white, safeFont);
            matrixRowTargetTexts[i] = CreateMatrixColumnText($"Col_Target_{i}", rowRect, 92f, 92f, 44f, "-", 15f, MatrixBrightCyanColor, safeFont);
            matrixRowMeasuredTexts[i] = CreateMatrixColumnText($"Col_Measured_{i}", rowRect, 184f, 86f, 44f, "-", 16f, Color.white, safeFont);
            matrixRowStatusTexts[i] = CreateMatrixColumnText($"Col_Status_{i}", rowRect, 270f, 162f, 44f, "-", 14f, Color.white, safeFont);
        }

        RefreshTruthTableTargets();
        ResetTruthTableScanResults();
    }

    private Canvas FindTargetCanvas()
    {
        GameObject canvasObj = GameObject.Find("Canvas");
        if (canvasObj != null)
        {
            Canvas namedCanvas = canvasObj.GetComponent<Canvas>();
            if (namedCanvas != null)
            {
                return namedCanvas;
            }
        }

        return FindAnyObjectByType<Canvas>();
    }

    private TMP_FontAsset ResolveSafeTMPFont()
    {
        if (txtLevelTitle != null && txtLevelTitle.font != null)
        {
            return txtLevelTitle.font;
        }

        if (txtMissionDesc != null && txtMissionDesc.font != null)
        {
            return txtMissionDesc.font;
        }

        if (txtFeedback != null && txtFeedback.font != null)
        {
            return txtFeedback.font;
        }

        return TMP_Settings.defaultFontAsset;
    }

    private static void CreateMatrixPanelAccentBar(RectTransform parent)
    {
        GameObject accentObj = new GameObject("AccentBar_Top", typeof(RectTransform), typeof(Image));
        accentObj.transform.SetParent(parent, false);

        RectTransform rect = accentObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, 4f);

        Image img = accentObj.GetComponent<Image>();
        img.color = MatrixAccentCyanColor;
        img.raycastTarget = false;
    }

    private static RectTransform CreateMatrixRowContainer(string name, RectTransform parent, float topY, float height, Color bgColor)
    {
        GameObject rowObj = new GameObject(name, typeof(RectTransform), typeof(Image));
        rowObj.transform.SetParent(parent, false);

        RectTransform rect = rowObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(14f, topY);
        rect.sizeDelta = new Vector2(432f, height);

        Image img = rowObj.GetComponent<Image>();
        img.color = bgColor;
        img.raycastTarget = false;

        return rect;
    }

    private static TMP_Text CreateMatrixText(string name, RectTransform parent, Vector2 pos, Vector2 size,
        string initialText, float fontSize, TextAlignmentOptions alignment, Color color, TMP_FontAsset fontAsset)
    {
        GameObject textObj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObj.transform.SetParent(parent, false);

        RectTransform rect = textObj.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = size;

        TextMeshProUGUI tmp = textObj.GetComponent<TextMeshProUGUI>();
        if (fontAsset != null)
        {
            tmp.font = fontAsset;
        }
        tmp.text = initialText;
        tmp.fontSize = fontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = alignment;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.raycastTarget = false;

        return tmp;
    }

    private static TMP_Text CreateMatrixColumnText(string name, RectTransform rowParent, float xPos, float width, float height,
        string initialText, float fontSize, Color color, TMP_FontAsset fontAsset)
    {
        return CreateMatrixText(name, rowParent, new Vector2(xPos, 0f), new Vector2(width, height),
            initialText, fontSize, TextAlignmentOptions.Center, color, fontAsset);
    }

    private void RefreshTruthTableTargets()
    {
        if (matrixRowTargetTexts[0] == null)
        {
            return;
        }

        LevelScenario activeLevel = (!isSandboxMode && levels != null && levels.Count > 0)
            ? levels[Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1)]
            : null;

        for (int i = 0; i < TruthTableRowCount; i++)
        {
            if (isSandboxMode || activeLevel == null || activeLevel.expectedTruthTable == null || i >= activeLevel.expectedTruthTable.Length)
            {
                matrixRowTargetTexts[i].text = "BEBAS";
                matrixRowTargetTexts[i].color = MatrixBrightCyanColor;
            }
            else
            {
                bool targetBit = activeLevel.expectedTruthTable[i];
                matrixRowTargetTexts[i].text = targetBit ? "1" : "0";
                matrixRowTargetTexts[i].color = Color.white;
            }
        }
    }

    private void ResetTruthTableScanResults()
    {
        scanningRowIndex = -1;

        for (int i = 0; i < TruthTableRowCount; i++)
        {
            matrixRowScannedFlags[i] = false;
            matrixRowPassedFlags[i] = false;

            if (matrixRowMeasuredTexts[i] != null)
            {
                matrixRowMeasuredTexts[i].text = "-";
                matrixRowMeasuredTexts[i].color = Color.white;
            }

            if (matrixRowStatusTexts[i] != null)
            {
                matrixRowStatusTexts[i].text = "-";
                matrixRowStatusTexts[i].color = Color.white;
            }

            if (matrixRowBackgrounds[i] != null)
            {
                matrixRowBackgrounds[i].color = RowIdleBgColor;
            }
        }
    }

    private bool HasAnyScannedMatrixRow()
    {
        for (int i = 0; i < TruthTableRowCount; i++)
        {
            if (matrixRowScannedFlags[i])
            {
                return true;
            }
        }

        return false;
    }

    private void SetTruthTableRowScanning(int rowIndex)
    {
        scanningRowIndex = rowIndex;
        if (rowIndex < 0 || rowIndex >= TruthTableRowCount)
        {
            return;
        }

        if (matrixRowMeasuredTexts[rowIndex] != null)
        {
            matrixRowMeasuredTexts[rowIndex].text = "...";
            matrixRowMeasuredTexts[rowIndex].color = Color.yellow;
        }

        if (matrixRowStatusTexts[rowIndex] != null)
        {
            matrixRowStatusTexts[rowIndex].text = "UJI...";
            matrixRowStatusTexts[rowIndex].color = Color.yellow;
        }

        if (matrixRowBackgrounds[rowIndex] != null)
        {
            matrixRowBackgrounds[rowIndex].color = RowScanningBgColor;
        }
    }

    private void SetTruthTableRowResult(int rowIndex, bool measuredOutput, bool passed)
    {
        if (rowIndex < 0 || rowIndex >= TruthTableRowCount)
        {
            return;
        }

        matrixRowScannedFlags[rowIndex] = true;
        matrixRowPassedFlags[rowIndex] = passed;

        if (matrixRowMeasuredTexts[rowIndex] != null)
        {
            matrixRowMeasuredTexts[rowIndex].text = measuredOutput ? "1" : "0";
            matrixRowMeasuredTexts[rowIndex].color = measuredOutput ? MatrixBrightGreenColor : MatrixBrightRedColor;
        }

        if (matrixRowStatusTexts[rowIndex] != null)
        {
            if (isSandboxMode)
            {
                matrixRowStatusTexts[rowIndex].text = "[TERCATAT]";
                matrixRowStatusTexts[rowIndex].color = MatrixBrightCyanColor;
            }
            else if (passed)
            {
                matrixRowStatusTexts[rowIndex].text = "[OK] LULUS";
                matrixRowStatusTexts[rowIndex].color = MatrixBrightGreenColor;
            }
            else
            {
                matrixRowStatusTexts[rowIndex].text = "[X] GAGAL";
                matrixRowStatusTexts[rowIndex].color = MatrixBrightRedColor;
            }
        }

        if (matrixRowBackgrounds[rowIndex] != null)
        {
            matrixRowBackgrounds[rowIndex].color = isSandboxMode
                ? RowSandboxDoneBgColor
                : (passed ? RowPassBgColor : RowFailBgColor);
        }
    }

    private void UpdateTruthTableMatrixLive()
    {
        if (txtBooleanFormula == null)
        {
            return;
        }

        string currentFormula = circuitManager != null
            ? circuitManager.GetCircuitBooleanExpression(outputNode)
            : "Y = (Belum Terhubung)";

        int activeGates = circuitManager != null ? circuitManager.activeGateCardsCount : 0;
        bool outputTracked = outputNode != null && outputNode.isTracked;
        string topologySignature = $"{currentFormula}|{activeGates}|{outputTracked}";

        if (!isVerifying)
        {
            if (hasLastTopologySignature && topologySignature != lastTopologySignature && HasAnyScannedMatrixRow())
            {
                ResetTruthTableScanResults();
            }

            lastTopologySignature = topologySignature;
            hasLastTopologySignature = true;
        }

        txtBooleanFormula.text = $"Rumus Rakitan: {currentFormula}";

        if (txtCurrentOutputReadout != null)
        {
            if (outputNode != null && outputNode.isTracked)
            {
                bool outputVal = outputNode.currentOutputValue;
                txtCurrentOutputReadout.text = outputVal ? "Output Y = 1 [AKTIF]" : "Output Y = 0 [MATI]";
                txtCurrentOutputReadout.color = outputVal ? MatrixBrightGreenColor : MatrixBrightRedColor;
            }
            else
            {
                txtCurrentOutputReadout.text = "Output Y = - [BELUM TERLACAK]";
                txtCurrentOutputReadout.color = new Color(1f, 0.72f, 0.25f, 1f);
            }
        }

        // Sorot baris kombinasi Input A & Input B yang aktif secara manual di luar proses scanning
        int activeManualRow = -1;
        if (!isVerifying)
        {
            bool anyInputTracked = (inputANode != null && inputANode.isTracked) || (inputBNode != null && inputBNode.isTracked);
            if (anyInputTracked)
            {
                bool curA = inputANode != null && inputANode.currentOutputValue;
                bool curB = inputBNode != null && inputBNode.currentOutputValue;
                activeManualRow = (curA ? 2 : 0) + (curB ? 1 : 0);
            }
        }

        for (int i = 0; i < TruthTableRowCount; i++)
        {
            if (matrixRowBackgrounds[i] == null)
            {
                continue;
            }

            if (i == scanningRowIndex)
            {
                matrixRowBackgrounds[i].color = RowScanningBgColor;
            }
            else if (!isVerifying && i == activeManualRow)
            {
                matrixRowBackgrounds[i].color = RowActiveManualBgColor;
            }
            else if (matrixRowScannedFlags[i])
            {
                matrixRowBackgrounds[i].color = isSandboxMode
                    ? RowSandboxDoneBgColor
                    : (matrixRowPassedFlags[i] ? RowPassBgColor : RowFailBgColor);
            }
            else
            {
                matrixRowBackgrounds[i].color = RowIdleBgColor;
            }
        }
    }

    /// <summary>
    /// Memastikan array pelacakan penyelesaian level sesuai dengan jumlah skenario level yang aktif.
    /// </summary>
    private void EnsureLevelCompletedFlagsInitialized()
    {
        int count = levels != null ? levels.Count : 0;
        if (levelCompletedFlags == null || levelCompletedFlags.Length != count)
        {
            levelCompletedFlags = new bool[count];
        }
    }

    /// <summary>
    /// Mengecek apakah seluruh level dalam daftar sudah pernah diselesaikan.
    /// </summary>
    private bool AreAllLevelsCompleted()
    {
        if (levelCompletedFlags == null || levelCompletedFlags.Length == 0)
        {
            return false;
        }

        for (int i = 0; i < levelCompletedFlags.Length; i++)
        {
            if (!levelCompletedFlags[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Memperbarui teks tombol Toggle Input A dan Input B secara real-time sesuai nilai output saat ini.
    /// </summary>
    private void UpdateInputToggleButtonsUI()
    {
        if (txtBtnToggleA != null && inputANode != null)
        {
            txtBtnToggleA.text = $"Input A: {(inputANode.currentOutputValue ? 1 : 0)}";
        }

        if (txtBtnToggleB != null && inputBNode != null)
        {
            txtBtnToggleB.text = $"Input B: {(inputBNode.currentOutputValue ? 1 : 0)}";
        }
    }

    /// <summary>
    /// Mengisi 5 skenario level berjenjang sesuai spesifikasi Kurikulum Merdeka Fase E.
    /// </summary>
    private void InitializeDefaultLevels()
    {
        levels = new List<LevelScenario>
        {
            // Level 1: Gerbang OR (Maks 1 Kartu) -> AB: 00=0, 01=1, 10=1, 11=1
            new LevelScenario
            {
                levelTitle = "Level 1: Alarm Pintu & Jendela",
                missionDescription = "Susun rangkaian agar Output = 1 jika A atau B bernilai 1",
                maxGateCardsQuota = 1,
                expectedTruthTable = new bool[] { false, true, true, true }
            },

            // Level 2: Gerbang XOR (Maks 1 Kartu) -> AB: 00=0, 01=1, 10=1, 11=0
            new LevelScenario
            {
                levelTitle = "Level 2: Sakelar Tangga Dua Arah",
                missionDescription = "Susun rangkaian agar Output = 1 jika posisi A dan B berbeda",
                maxGateCardsQuota = 1,
                expectedTruthTable = new bool[] { false, true, true, false }
            },

            // Level 3: Kombinasi A AND (NOT B) (Maks 2 Kartu) -> AB: 00=0, 01=0, 10=1, 11=0
            new LevelScenario
            {
                levelTitle = "Level 3: Kunci Keamanan Brankas",
                missionDescription = "Susun rangkaian agar Output = 1 hanya saat A = 1 dan B = 0",
                maxGateCardsQuota = 2,
                expectedTruthTable = new bool[] { false, false, true, false }
            },

            // Level 4: Gerbang NAND (Maks 1 Kartu) -> !(A && B): 00=1, 01=1, 10=1, 11=0
            new LevelScenario
            {
                levelTitle = "Level 4: Pemutus Darurat Reaktor",
                missionDescription = "Susun rangkaian agar Output = 0 hanya saat A = 1 dan B = 1",
                maxGateCardsQuota = 1,
                expectedTruthTable = new bool[] { true, true, true, false }
            },

            // Level 5 Boss: Gerbang XNOR (XOR -> NOT, Maks 2 Kartu) -> (A == B): 00=1, 01=0, 10=0, 11=1
            new LevelScenario
            {
                levelTitle = "Level 5: Sinkronisasi Inti Kembar",
                missionDescription = "Susun rangkaian agar Output = 1 saat A dan B bernilai SAMA",
                maxGateCardsQuota = 2,
                expectedTruthTable = new bool[] { true, false, false, true }
            }
        };

        levelCompletedFlags = new bool[levels.Count];
    }

    /// <summary>
    /// Memperbarui teks kuota gerbang secara real-time di setiap frame:
    /// Di Mode Sandbox bebas batas kuota, sedangkan di Level 1..5 merah bila melebihi kuota.
    /// </summary>
    private void UpdateQuotaStatusUI()
    {
        if (txtQuotaStatus == null || circuitManager == null)
        {
            return;
        }

        int activeCount = circuitManager.activeGateCardsCount;

        if (isSandboxMode)
        {
            bool supportsInfinityGlyph = txtQuotaStatus.font != null && txtQuotaStatus.font.HasCharacter('\u221E');
            string unlimitedSymbol = supportsInfinityGlyph ? "\u221E" : "Bebas";
            txtQuotaStatus.text = $"Kuota Gerbang: {activeCount} / {unlimitedSymbol}";
            txtQuotaStatus.color = new Color(0.55f, 1f, 0.92f, 1f);
            return;
        }

        if (levels == null || levels.Count == 0)
        {
            return;
        }

        int safeIndex = Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1);
        LevelScenario currentLevel = levels[safeIndex];
        int maxQuota = currentLevel.maxGateCardsQuota;

        txtQuotaStatus.text = $"Kuota Gerbang: {activeCount} / {maxQuota}";
        txtQuotaStatus.color = (activeCount > maxQuota) ? Color.red : Color.white;
    }

    /// <summary>
    /// Memuat data skenario level berdasarkan indeks, menyinkronkan varian diorama 3D OutputActuator,
    /// mereset matrix scanner, dan memperbarui seluruh komponen HUD UI.
    /// </summary>
    public void LoadLevel(int index)
    {
        StopAllCoroutines();
        isVerifying = false;
        isSandboxMode = false;
        awaitingPostVictoryChoice = false;

        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(false);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(false);
        }

        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }
        else
        {
            EnsureLevelCompletedFlagsInitialized();
        }

        currentLevelIndex = Mathf.Clamp(index, 0, levels.Count - 1);
        LevelScenario currentLevel = levels[currentLevelIndex];

        if (outputNode == null)
        {
            AutoBindCoreNodesIfNeeded();
        }

        if (outputNode != null)
        {
            outputNode.SetOutputDioramaVariant(currentLevelIndex);
        }

        AutoBindActionButtonsIfNeeded();
        InitializeTruthTableMatrixUI();

        if (txtNextLevelButtonLabel != null)
        {
            txtNextLevelButtonLabel.text = "Level Berikutnya";
        }

        if (nextLevelButtonObj != null)
        {
            nextLevelButtonObj.SetActive(false);
        }

        if (txtResetLevelButtonLabel != null)
        {
            txtResetLevelButtonLabel.text = "Reset Level";
        }

        if (resetLevelButtonObj != null)
        {
            resetLevelButtonObj.SetActive(true);
        }

        if (txtLevelTitle != null)
        {
            txtLevelTitle.text = $"[{currentLevelIndex + 1}/{levels.Count}] {currentLevel.levelTitle}";
        }

        if (txtMissionDesc != null)
        {
            txtMissionDesc.text = currentLevel.missionDescription;
        }

        if (txtFeedback != null)
        {
            txtFeedback.text = "Susun kartu fisik di atas meja, lalu tekan tombol Uji Rangkaian.";
            txtFeedback.color = Color.white;
        }

        RefreshTruthTableTargets();
        ResetTruthTableScanResults();
        UpdateQuotaStatusUI();
        UpdateInputToggleButtonsUI();
    }

    /// <summary>
    /// Mengaktifkan Mode Sandbox Bebas tanpa batasan kuota dengan Diorama 4 (Twin Sync Cyber Obelisk).
    /// </summary>
    public void EnterSandboxMode()
    {
        StopAllCoroutines();
        isVerifying = false;
        isSandboxMode = true;
        awaitingPostVictoryChoice = false;

        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(false);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(false);
        }

        if (outputNode == null)
        {
            AutoBindCoreNodesIfNeeded();
        }

        if (outputNode != null)
        {
            outputNode.SetOutputDioramaVariant(4);
        }

        AutoBindActionButtonsIfNeeded();
        InitializeTruthTableMatrixUI();

        if (nextLevelButtonObj != null)
        {
            nextLevelButtonObj.SetActive(false);
        }

        if (txtResetLevelButtonLabel != null)
        {
            txtResetLevelButtonLabel.text = "Main Ulang (Level 1)";
        }

        if (resetLevelButtonObj != null)
        {
            resetLevelButtonObj.SetActive(true);
        }

        if (txtLevelTitle != null)
        {
            txtLevelTitle.text = "[SANDBOX] Laboratorium Rangkaian Bebas";
        }

        if (txtMissionDesc != null)
        {
            txtMissionDesc.text = "Eksplorasi bebas tanpa batas kuota! Tekan Uji untuk memindai 4 baris output.";
        }

        if (txtFeedback != null)
        {
            txtFeedback.text = "Mode Sandbox aktif. Susun gerbang apa pun lalu tekan Uji Rangkaian.";
            txtFeedback.color = Color.white;
        }

        RefreshTruthTableTargets();
        ResetTruthTableScanResults();
        UpdateQuotaStatusUI();
        UpdateInputToggleButtonsUI();
    }

    /// <summary>
    /// Berpindah ke skenario level berikutnya, atau masuk ke Mode Sandbox Bebas setelah seluruh misi selesai.
    /// </summary>
    public void NextLevel()
    {
        if (levels == null || levels.Count == 0)
        {
            return;
        }

        if (isSandboxMode)
        {
            RestartFromFirstLevel();
            return;
        }

        EnsureLevelCompletedFlagsInitialized();

        bool isLastLevelCleared = (currentLevelIndex == levels.Count - 1) && levelCompletedFlags[currentLevelIndex];
        if (awaitingPostVictoryChoice || AreAllLevelsCompleted() || isLastLevelCleared)
        {
            EnterSandboxMode();
            return;
        }

        int nextIndex = (currentLevelIndex + 1) % levels.Count;
        LoadLevel(nextIndex);
    }

    /// <summary>
    /// Mengulang misi aktif dari awal. Jika dipanggil setelah kemenangan Level 5 atau di Mode Sandbox,
    /// mengembalikan permainan ke Level 1 serta mereset progres dan nilai input.
    /// </summary>
    public void ResetCurrentLevel()
    {
        if (awaitingPostVictoryChoice || isSandboxMode)
        {
            RestartFromFirstLevel();
            return;
        }

        LoadLevel(currentLevelIndex);
    }

    private void RestartFromFirstLevel()
    {
        EnsureLevelCompletedFlagsInitialized();
        Array.Clear(levelCompletedFlags, 0, levelCompletedFlags.Length);

        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(false);
            inputANode.SetInputState(false);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(false);
            inputBNode.SetInputState(false);
        }

        LoadLevel(0);
    }

    /// <summary>
    /// Dipanggil oleh tombol UI "Uji Rangkaian".
    /// Menjalankan Coroutine VerifyTruthTableRoutine() jika belum ada proses pemindaian yang berjalan.
    /// </summary>
    public void OnVerifyButtonClicked()
    {
        if (isVerifying)
        {
            return;
        }

        StartCoroutine(VerifyTruthTableRoutine());
    }

    /// <summary>
    /// Coroutine penguji otomatis 4 baris Tabel Kebenaran (AB: 00, 01, 10, 11)
    /// untuk Level 1..5 maupun pemindaian bebas di Mode Sandbox, terhubung langsung dengan Scanner Matrix UI.
    /// </summary>
    private IEnumerator VerifyTruthTableRoutine()
    {
        isVerifying = true;

        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }

        LevelScenario currentLevel = levels[Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1)];

        if (circuitManager != null)
        {
            circuitManager.EvaluateCircuit();
        }

        // Tahap A: Validasi Kuota Kartu Gerbang (dilewati di Mode Sandbox Bebas)
        if (!isSandboxMode && circuitManager != null && circuitManager.activeGateCardsCount > currentLevel.maxGateCardsQuota)
        {
            if (txtFeedback != null)
            {
                txtFeedback.text = $"Gagal! Melebihi kuota maksimal gerbang ({circuitManager.activeGateCardsCount}/{currentLevel.maxGateCardsQuota}).";
                txtFeedback.color = Color.red;
            }

            isVerifying = false;
            yield break;
        }

        // Tahap B: Validasi Koneksi Input, Gerbang, dan Output Actuator
        if (isSandboxMode)
        {
            bool anyInputTracked = (inputANode != null && inputANode.isTracked) || (inputBNode != null && inputBNode.isTracked);
            if (!anyInputTracked)
            {
                if (txtFeedback != null)
                {
                    txtFeedback.text = "Arahkan kamera ke minimal satu kartu Input (A/B) dan kartu Output untuk memindai.";
                    txtFeedback.color = new Color(1f, 0.5f, 0f);
                }

                isVerifying = false;
                yield break;
            }
        }
        else
        {
            if (inputANode == null || !inputANode.isTracked || inputBNode == null || !inputBNode.isTracked)
            {
                if (txtFeedback != null)
                {
                    txtFeedback.text = "Gagal! Pastikan kartu Input A dan Input B keduanya sudah terdeteksi kamera.";
                    txtFeedback.color = new Color(1f, 0.5f, 0f);
                }

                isVerifying = false;
                yield break;
            }

            if (circuitManager != null && circuitManager.allNodes != null)
            {
                for (int i = 0; i < circuitManager.allNodes.Count; i++)
                {
                    GateNode node = circuitManager.allNodes[i];
                    if (node != null && node.isTracked && node.nodeType != NodeType.InputSource && node.nodeType != NodeType.OutputActuator)
                    {
                        int requiredInputs = node.GetMaxInputCount();
                        if (node.connectedInputs.Count < requiredInputs)
                        {
                            if (txtFeedback != null)
                            {
                                txtFeedback.text = $"Gagal! Gerbang {node.nodeType} membutuhkan {requiredInputs} input (baru terhubung {node.connectedInputs.Count}). Dekatkan kartu Input.";
                                txtFeedback.color = new Color(1f, 0.5f, 0f);
                            }

                            isVerifying = false;
                            yield break;
                        }
                    }
                }
            }
        }

        if (outputNode == null || !outputNode.isTracked || outputNode.connectedInputs.Count == 0)
        {
            if (txtFeedback != null)
            {
                txtFeedback.text = "Gagal! Kartu Output belum terdeteksi kamera atau belum tersambung ke rangkaian.";
                txtFeedback.color = new Color(1f, 0.5f, 0f);
            }

            isVerifying = false;
            yield break;
        }

        // Tahap C: Pindai / Uji 4 Kombinasi Input Biner AB (00, 01, 10, 11)
        ResetTruthTableScanResults();

        bool originalInputA = inputANode != null && inputANode.initialInputState;
        bool originalInputB = inputBNode != null && inputBNode.initialInputState;

        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(true);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(true);
        }

        bool[] scannedOutputs = new bool[TruthTableRowCount];

        for (int i = 0; i < TruthTableRowCount; i++)
        {
            bool testA = (i == 2 || i == 3);
            bool testB = (i == 1 || i == 3);

            SetTruthTableRowScanning(i);

            if (inputANode != null)
            {
                inputANode.SetInputState(testA);
            }

            if (inputBNode != null)
            {
                inputBNode.SetInputState(testB);
            }

            if (circuitManager != null)
            {
                circuitManager.EvaluateCircuit();
            }

            if (txtFeedback != null)
            {
                txtFeedback.text = isSandboxMode
                    ? $"Memindai baris {i + 1}/4: A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)}..."
                    : $"Menguji baris {i + 1}/4: A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)}...";
                txtFeedback.color = Color.yellow;
            }

            yield return new WaitForSeconds(0.45f);

            if (circuitManager != null)
            {
                circuitManager.EvaluateCircuit();
            }

            bool actualOutput = outputNode.currentOutputValue;
            scannedOutputs[i] = actualOutput;

            bool expectedOutput = (!isSandboxMode && currentLevel.expectedTruthTable != null && i < currentLevel.expectedTruthTable.Length)
                                  && currentLevel.expectedTruthTable[i];
            bool rowPassed = isSandboxMode || (actualOutput == expectedOutput);

            SetTruthTableRowResult(i, actualOutput, rowPassed);

            if (!isSandboxMode && !rowPassed)
            {
                scanningRowIndex = -1;
                RestoreInitialInputStates(originalInputA, originalInputB);

                if (txtFeedback != null)
                {
                    txtFeedback.text = $"Gagal pada uji A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)} (Hasil: {(actualOutput ? 1 : 0)}, Target: {(expectedOutput ? 1 : 0)}).";
                    txtFeedback.color = Color.red;
                }

                isVerifying = false;
                yield break;
            }
        }

        // Tahap D: Hasil Akhir Pemindaian / Verifikasi
        scanningRowIndex = -1;
        RestoreInitialInputStates(originalInputA, originalInputB);

        if (isSandboxMode)
        {
            if (txtFeedback != null)
            {
                string r0 = scannedOutputs[0] ? "1" : "0";
                string r1 = scannedOutputs[1] ? "1" : "0";
                string r2 = scannedOutputs[2] ? "1" : "0";
                string r3 = scannedOutputs[3] ? "1" : "0";
                txtFeedback.text = $"Hasil Pindai Sandbox -> [00:{r0}] [01:{r1}] [10:{r2}] [11:{r3}]";
                txtFeedback.color = new Color(0.35f, 1f, 0.85f, 1f);
            }

            isVerifying = false;
            yield break;
        }

        EnsureLevelCompletedFlagsInitialized();
        if (currentLevelIndex >= 0 && currentLevelIndex < levelCompletedFlags.Length)
        {
            levelCompletedFlags[currentLevelIndex] = true;
        }

        bool isLastLevel = (levels != null && levels.Count > 0 && currentLevelIndex == levels.Count - 1);
        bool isGameCleared = AreAllLevelsCompleted() || isLastLevel;

        AutoBindActionButtonsIfNeeded();

        if (isGameCleared)
        {
            awaitingPostVictoryChoice = true;

            if (txtFeedback != null)
            {
                txtFeedback.text = $"SELAMAT! Seluruh {levels.Count} Misi GateWeaver AR Berhasil Diselesaikan!";
                txtFeedback.color = Color.green;
            }

            if (txtNextLevelButtonLabel != null)
            {
                txtNextLevelButtonLabel.text = "Mode Sandbox Bebas";
            }

            if (nextLevelButtonObj != null)
            {
                nextLevelButtonObj.SetActive(true);
            }

            if (txtResetLevelButtonLabel != null)
            {
                txtResetLevelButtonLabel.text = "Main Ulang (Level 1)";
            }

            if (resetLevelButtonObj != null)
            {
                resetLevelButtonObj.SetActive(true);
            }
        }
        else
        {
            awaitingPostVictoryChoice = false;

            if (txtFeedback != null)
            {
                txtFeedback.text = "BERHASIL! Rangkaian lolos seluruh 4 baris Tabel Kebenaran.";
                txtFeedback.color = Color.green;
            }

            if (txtNextLevelButtonLabel != null)
            {
                txtNextLevelButtonLabel.text = "Level Berikutnya";
            }

            if (nextLevelButtonObj != null)
            {
                nextLevelButtonObj.SetActive(true);
            }
        }

        isVerifying = false;
    }

    private void RestoreInitialInputStates(bool stateA, bool stateB)
    {
        if (inputANode != null)
        {
            inputANode.SetSimulationOverride(false);
            inputANode.SetInputState(stateA);
        }

        if (inputBNode != null)
        {
            inputBNode.SetSimulationOverride(false);
            inputBNode.SetInputState(stateB);
        }

        if (circuitManager != null)
        {
            circuitManager.EvaluateCircuit();
        }
    }

    /// <summary>
    /// Helper opsional untuk tombol UI Toggle Input A (dinonaktifkan sementara saat sedang verifikasi).
    /// </summary>
    public void OnToggleInputAClicked()
    {
        if (!isVerifying && inputANode != null)
        {
            inputANode.ToggleInputState();
        }
    }

    /// <summary>
    /// Helper opsional untuk tombol UI Toggle Input B (dinonaktifkan sementara saat sedang verifikasi).
    /// </summary>
    public void OnToggleInputBClicked()
    {
        if (!isVerifying && inputBNode != null)
        {
            inputBNode.ToggleInputState();
        }
    }
}
