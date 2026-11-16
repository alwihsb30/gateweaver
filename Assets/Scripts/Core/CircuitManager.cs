using System.Collections.Generic;
using UnityEngine;

public class CircuitManager : MonoBehaviour
{
    [Header("1. Daftar Node & Parameter Spasial")]
    public List<GateNode> allNodes = new List<GateNode>();
    public float maxSnapDistance = 3.0f;
    public float inputSourceRangeMultiplier = 1.8f;

    [Header("2. Status Runtime Rangkaian")]
    public int activeGateCardsCount = 0;

    private readonly List<GateNode> trackedNodes = new List<GateNode>();

    private void Awake()
    {
        allNodes.RemoveAll(node => node == null);
        if (allNodes.Count == 0)
        {
#if UNITY_6000_5_OR_NEWER
            GateNode[] foundNodes = FindObjectsByType<GateNode>();
#else
            GateNode[] foundNodes = FindObjectsByType<GateNode>(FindObjectsSortMode.None);
#endif
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
            GateNode node = allNodes[i];
            if (node == null) continue;
            node.connectedInputs.Clear();
            node.connectedTarget = null;
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
            float xA = GetCameraRelativeX(a.transform.position, camTransform);
            float xB = GetCameraRelativeX(b.transform.position, camTransform);
            return xA.CompareTo(xB);
        });

        float maxInputReach = maxSnapDistance * Mathf.Max(1f, inputSourceRangeMultiplier);
        RouteInputSources(camTransform, maxInputReach);
        RouteLogicGates(camTransform);

        int maxPasses = Mathf.Max(1, trackedNodes.Count);
        for (int pass = 0; pass < maxPasses; pass++)
        {
            bool anyChanged = false;
            for (int i = 0; i < trackedNodes.Count; i++)
            {
                bool previousValue = trackedNodes[i].currentOutputValue;
                trackedNodes[i].EvaluateGateLogic();
                if (trackedNodes[i].currentOutputValue != previousValue)
                {
                    anyChanged = true;
                }
            }

            if (!anyChanged) break;
        }

        for (int i = 0; i < allNodes.Count; i++)
        {
            if (allNodes[i] != null)
            {
                allNodes[i].UpdateVisuals();
            }
        }
    }

    private void RouteInputSources(Transform camTransform, float maxInputReach)
    {
        bool hasActiveLogicGates = activeGateCardsCount > 0;
        AssignClosestInputPairs(camTransform, maxInputReach, hasActiveLogicGates, requireRightward: true);
        AssignClosestInputPairs(camTransform, maxInputReach, hasActiveLogicGates, requireRightward: false);
    }

    private void AssignClosestInputPairs(Transform camTransform, float maxRange, bool hasActiveLogicGates, bool requireRightward)
    {
        const float HorizontalAlignmentTolerance = 0.15f;

        while (true)
        {
            GateNode bestSource = null;
            GateNode bestTarget = null;
            float bestDistance = maxRange;

            for (int i = 0; i < trackedNodes.Count; i++)
            {
                GateNode source = trackedNodes[i];
                if (source.nodeType != NodeType.InputSource || source.connectedTarget != null) continue;

                float sourceX = GetCameraRelativeX(source.transform.position, camTransform);

                for (int j = 0; j < trackedNodes.Count; j++)
                {
                    GateNode candidate = trackedNodes[j];
                    if (candidate == source || candidate.nodeType == NodeType.InputSource) continue;
                    if (hasActiveLogicGates && candidate.nodeType == NodeType.OutputActuator) continue;
                    if (hasActiveLogicGates && !IsLogicGateCard(candidate.nodeType)) continue;
                    if (candidate.connectedInputs.Count >= candidate.GetMaxInputCount()) continue;

                    if (requireRightward)
                    {
                        float candidateX = GetCameraRelativeX(candidate.transform.position, camTransform);
                        if (candidateX < sourceX - HorizontalAlignmentTolerance) continue;
                    }

                    float distance = Vector3.Distance(source.transform.position, candidate.transform.position);
                    if (distance <= bestDistance)
                    {
                        bestDistance = distance;
                        bestSource = source;
                        bestTarget = candidate;
                    }
                }
            }

            if (bestSource == null || bestTarget == null) break;

            bestSource.connectedTarget = bestTarget;
            bestTarget.connectedInputs.Add(bestSource);
        }
    }

    private void RouteLogicGates(Transform camTransform)
    {
        RouteGatesToTargets(camTransform, targetGatesOnly: true, requireRightward: true);
        RouteGatesToTargets(camTransform, targetGatesOnly: true, requireRightward: false);
        RouteGatesToTargets(camTransform, targetGatesOnly: false, requireRightward: true);
        RouteGatesToTargets(camTransform, targetGatesOnly: false, requireRightward: false);
    }

    private void RouteGatesToTargets(Transform camTransform, bool targetGatesOnly, bool requireRightward)
    {
        const float HorizontalAlignmentTolerance = 0.15f;

        for (int i = trackedNodes.Count - 1; i >= 0; i--)
        {
            GateNode source = trackedNodes[i];
            if (!IsLogicGateCard(source.nodeType) || source.connectedTarget != null) continue;

            float sourceX = GetCameraRelativeX(source.transform.position, camTransform);
            GateNode bestTarget = null;
            float closestDistance = maxSnapDistance;

            for (int j = 0; j < trackedNodes.Count; j++)
            {
                GateNode candidate = trackedNodes[j];
                if (candidate == null || candidate == source || candidate.nodeType == NodeType.InputSource) continue;

                if (targetGatesOnly)
                {
                    if (!IsLogicGateCard(candidate.nodeType)) continue;
                }
                else if (candidate.nodeType != NodeType.OutputActuator)
                {
                    continue;
                }

                if (candidate.connectedInputs.Count >= candidate.GetMaxInputCount()) continue;

                if (requireRightward)
                {
                    float candidateX = GetCameraRelativeX(candidate.transform.position, camTransform);
                    if (candidateX < sourceX - HorizontalAlignmentTolerance) continue;
                }

                if (WouldCreateCycle(source, candidate)) continue;

                float distance = Vector3.Distance(source.transform.position, candidate.transform.position);
                if (distance <= closestDistance)
                {
                    closestDistance = distance;
                    bestTarget = candidate;
                }
            }

            if (bestTarget != null)
            {
                source.connectedTarget = bestTarget;
                bestTarget.connectedInputs.Add(source);
            }
        }
    }

    private bool WouldCreateCycle(GateNode source, GateNode target)
    {
        GateNode current = target;
        int safetyCounter = trackedNodes.Count + 1;
        while (current != null && safetyCounter > 0)
        {
            if (current == source) return true;
            current = current.connectedTarget;
            safetyCounter--;
        }
        return false;
    }

    private float GetCameraRelativeX(Vector3 worldPosition, Transform cameraTransform)
    {
        return cameraTransform != null ? cameraTransform.InverseTransformPoint(worldPosition).x : worldPosition.x;
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
