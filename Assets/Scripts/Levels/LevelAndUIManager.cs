using System;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

[Serializable]
public class LevelScenario
{
    public string levelTitle;
    [TextArea(2, 4)]
    public string missionDescription;
    public int maxGateCardsQuota = 1;
    public bool[] expectedTruthTable = new bool[4];
}

public class LevelAndUIManager : MonoBehaviour
{
    [Header("1. Referensi Sistem & Node Inti")]
    public CircuitManager circuitManager;
    public GateNode inputANode;
    public GateNode inputBNode;
    public GateNode outputNode;

    [Header("2. Referensi Komponen UI (TextMeshPro & Tombol)")]
    public TMP_Text txtLevelTitle;
    public TMP_Text txtMissionDesc;
    public TMP_Text txtQuotaStatus;
    public TMP_Text txtFeedback;
    public GameObject nextLevelButtonObj;
    public GameObject resetLevelButtonObj;
    public TMP_Text txtBtnToggleA;
    public TMP_Text txtBtnToggleB;

    [Header("3. Daftar Skenario Level")]
    public List<LevelScenario> levels = new List<LevelScenario>();
    public int currentLevelIndex = 0;

    protected bool isVerifying = false;

    private void Awake()
    {
        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }

        if (circuitManager == null)
        {
            circuitManager = FindAnyObjectByType<CircuitManager>();
        }

        AutoBindCoreNodesIfNeeded();
        AutoBindToggleButtonsTextIfNeeded();
    }

    private void Start()
    {
        LoadLevel(currentLevelIndex);
    }

    private void Update()
    {
        UpdateQuotaStatusUI();
        UpdateInputToggleButtonsUI();
    }

    private void AutoBindCoreNodesIfNeeded()
    {
        if (inputANode != null && inputBNode != null && outputNode != null)
        {
            return;
        }

        GateNode[] nodes = FindObjectsByType<GateNode>(FindObjectsSortMode.None);
        for (int i = 0; i < nodes.Length; i++)
        {
            GateNode node = nodes[i];
            if (node == null) continue;

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

    private void AutoBindToggleButtonsTextIfNeeded()
    {
        if (txtBtnToggleA == null)
        {
            GameObject btnA = GameObject.Find("Btn_ToggleInputA");
            if (btnA != null) txtBtnToggleA = btnA.GetComponentInChildren<TMP_Text>(true);
        }

        if (txtBtnToggleB == null)
        {
            GameObject btnB = GameObject.Find("Btn_ToggleInputB");
            if (btnB != null) txtBtnToggleB = btnB.GetComponentInChildren<TMP_Text>(true);
        }
    }

    private void InitializeDefaultLevels()
    {
        levels = new List<LevelScenario>
        {
            new LevelScenario
            {
                levelTitle = "Level 1: Alarm Pintu & Jendela",
                missionDescription = "Susun rangkaian agar Output = 1 jika A atau B bernilai 1",
                maxGateCardsQuota = 1,
                expectedTruthTable = new bool[] { false, true, true, true }
            },
            new LevelScenario
            {
                levelTitle = "Level 2: Sakelar Tangga Dua Arah",
                missionDescription = "Susun rangkaian agar Output = 1 jika posisi A dan B berbeda",
                maxGateCardsQuota = 1,
                expectedTruthTable = new bool[] { false, true, true, false }
            },
            new LevelScenario
            {
                levelTitle = "Level 3: Kunci Keamanan Brankas",
                missionDescription = "Susun rangkaian agar Output = 1 hanya saat A = 1 dan B = 0",
                maxGateCardsQuota = 2,
                expectedTruthTable = new bool[] { false, false, true, false }
            }
        };
    }

    private void UpdateQuotaStatusUI()
    {
        if (txtQuotaStatus == null || circuitManager == null || levels == null || levels.Count == 0)
        {
            return;
        }

        int activeCount = circuitManager.activeGateCardsCount;
        int maxQuota = levels[Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1)].maxGateCardsQuota;
        txtQuotaStatus.text = $"Kuota Gerbang: {activeCount} / {maxQuota}";
        txtQuotaStatus.color = (activeCount > maxQuota) ? Color.red : Color.white;
    }

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

    public virtual void LoadLevel(int index)
    {
        if (levels == null || levels.Count == 0)
        {
            InitializeDefaultLevels();
        }

        currentLevelIndex = Mathf.Clamp(index, 0, levels.Count - 1);
        LevelScenario currentLevel = levels[currentLevelIndex];

        if (nextLevelButtonObj != null) nextLevelButtonObj.SetActive(false);
        if (txtLevelTitle != null) txtLevelTitle.text = currentLevel.levelTitle;
        if (txtMissionDesc != null) txtMissionDesc.text = currentLevel.missionDescription;
        if (txtFeedback != null)
        {
            txtFeedback.text = "Susun kartu fisik di atas meja, lalu uji rangkaian.";
            txtFeedback.color = Color.white;
        }
    }

    public virtual void NextLevel()
    {
        if (levels == null || levels.Count == 0) return;
        LoadLevel((currentLevelIndex + 1) % levels.Count);
    }

    public void OnToggleInputAClicked()
    {
        if (!isVerifying && inputANode != null) inputANode.ToggleInputState();
    }

    public void OnToggleInputBClicked()
    {
        if (!isVerifying && inputBNode != null) inputBNode.ToggleInputState();
    }
}
