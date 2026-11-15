using System;
using System.Collections;
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
        StopAllCoroutines();
        isVerifying = false;

        if (inputANode != null) inputANode.SetSimulationOverride(false);
        if (inputBNode != null) inputBNode.SetSimulationOverride(false);

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

    public void OnVerifyButtonClicked()
    {
        if (!isVerifying)
        {
            StartCoroutine(VerifyTruthTableRoutine());
        }
    }

    private IEnumerator VerifyTruthTableRoutine()
    {
        isVerifying = true;
        LevelScenario currentLevel = levels[Mathf.Clamp(currentLevelIndex, 0, levels.Count - 1)];

        if (circuitManager != null)
        {
            circuitManager.EvaluateCircuit();
        }

        if (circuitManager != null && circuitManager.activeGateCardsCount > currentLevel.maxGateCardsQuota)
        {
            if (txtFeedback != null)
            {
                txtFeedback.text = $"Gagal! Melebihi kuota maksimal gerbang ({circuitManager.activeGateCardsCount}/{currentLevel.maxGateCardsQuota}).";
                txtFeedback.color = Color.red;
            }
            isVerifying = false;
            yield break;
        }

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

        if (outputNode == null || !outputNode.isTracked || outputNode.connectedInputs.Count == 0)
        {
            if (txtFeedback != null)
            {
                txtFeedback.text = "Gagal! Kartu Output belum terdeteksi kamera atau belum tersambung.";
                txtFeedback.color = new Color(1f, 0.5f, 0f);
            }
            isVerifying = false;
            yield break;
        }

        bool originalInputA = inputANode.initialInputState;
        bool originalInputB = inputBNode.initialInputState;
        inputANode.SetSimulationOverride(true);
        inputBNode.SetSimulationOverride(true);

        for (int i = 0; i < 4; i++)
        {
            bool testA = (i == 2 || i == 3);
            bool testB = (i == 1 || i == 3);

            inputANode.SetInputState(testA);
            inputBNode.SetInputState(testB);
            if (circuitManager != null) circuitManager.EvaluateCircuit();

            if (txtFeedback != null)
            {
                txtFeedback.text = $"Menguji baris {i + 1}/4: A={(testA ? 1 : 0)}, B={(testB ? 1 : 0)}...";
                txtFeedback.color = Color.yellow;
            }

            yield return new WaitForSeconds(0.45f);
            if (circuitManager != null) circuitManager.EvaluateCircuit();

            bool actualOutput = outputNode.currentOutputValue;
            bool expectedOutput = currentLevel.expectedTruthTable[i];
            if (actualOutput != expectedOutput)
            {
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

        RestoreInitialInputStates(originalInputA, originalInputB);
        if (txtFeedback != null)
        {
            txtFeedback.text = "BERHASIL! Rangkaian lolos seluruh 4 baris Tabel Kebenaran.";
            txtFeedback.color = Color.green;
        }
        if (nextLevelButtonObj != null) nextLevelButtonObj.SetActive(true);

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

    public void OnToggleInputAClicked()
    {
        if (!isVerifying && inputANode != null) inputANode.ToggleInputState();
    }

    public void OnToggleInputBClicked()
    {
        if (!isVerifying && inputBNode != null) inputBNode.ToggleInputState();
    }
}
