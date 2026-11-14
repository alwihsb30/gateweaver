using System.Collections.Generic;
using UnityEngine;

public class CircuitManager : MonoBehaviour
{
    public List<GateNode> allNodes = new List<GateNode>();
    public float maxSnapDistance = 1.5f;
    public int activeGateCardsCount = 0;

    private readonly List<GateNode> trackedNodes = new List<GateNode>();

    private void Awake()
    {
        allNodes.RemoveAll(node => node == null);
        if (allNodes.Count == 0)
        {
            GateNode[] foundNodes = FindObjectsByType<GateNode>(FindObjectsSortMode.None);
            allNodes.AddRange(foundNodes);
        }
    }

    private void Update()
    {
        EvaluateCircuit();
    }

    public void EvaluateCircuit()
    {
        if (allNodes == null || allNodes.Count == 0)
        {
            activeGateCardsCount = 0;
            return;
        }

        for (int i = 0; i < allNodes.Count; i++)
        {
            if (allNodes[i] != null)
            {
                allNodes[i].connectedInputs.Clear();
                allNodes[i].connectedTarget = null;
            }
        }

        trackedNodes.Clear();
        int gateCount = 0;
        for (int i = 0; i < allNodes.Count; i++)
        {
            GateNode node = allNodes[i];
            if (node != null && node.isTracked)
            {
                trackedNodes.Add(node);
                if (IsLogicGateCard(node.nodeType))
                {
                    gateCount++;
                }
            }
        }

        activeGateCardsCount = gateCount;

        Transform camTransform = Camera.main != null ? Camera.main.transform : null;
        trackedNodes.Sort((a, b) =>
        {
            float xA = camTransform != null ? camTransform.InverseTransformPoint(a.transform.position).x : a.transform.position.x;
            float xB = camTransform != null ? camTransform.InverseTransformPoint(b.transform.position).x : b.transform.position.x;
            return xA.CompareTo(xB);
        });

        for (int i = 0; i < trackedNodes.Count; i++)
        {
            GateNode source = trackedNodes[i];
            if (source.nodeType == NodeType.OutputActuator)
            {
                continue;
            }

            GateNode bestTarget = null;
            float bestDist = maxSnapDistance;
            for (int j = i + 1; j < trackedNodes.Count; j++)
            {
                GateNode candidate = trackedNodes[j];
                if (candidate.nodeType == NodeType.InputSource)
                {
                    continue;
                }

                if (candidate.connectedInputs.Count >= candidate.GetMaxInputCount())
                {
                    continue;
                }

                float d = Vector3.Distance(source.transform.position, candidate.transform.position);
                if (d <= bestDist)
                {
                    bestDist = d;
                    bestTarget = candidate;
                }
            }

            if (bestTarget != null)
            {
                source.connectedTarget = bestTarget;
                bestTarget.connectedInputs.Add(source);
            }
        }

        for (int pass = 0; pass < Mathf.Max(1, trackedNodes.Count); pass++)
        {
            for (int i = 0; i < trackedNodes.Count; i++)
            {
                trackedNodes[i].EvaluateGateLogic();
            }
        }

        for (int i = 0; i < allNodes.Count; i++)
        {
            if (allNodes[i] != null)
            {
                allNodes[i].UpdateVisuals();
            }
        }
    }

    private bool IsLogicGateCard(NodeType type)
    {
        return type == NodeType.AND
            || type == NodeType.OR
            || type == NodeType.NOT
            || type == NodeType.XOR
            || type == NodeType.NAND;
    }
}
