#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Validator aset Prop Sci-Fi Melayang Mode Udara (GateWeaver AR v8.0 Tahap 7):
/// - Memverifikasi ke-7 aset .obj/.fbx di Assets/Resources/Models/V8_AirProps/ tersedia sebelum build APK Android,
///   memenuhi budget geometri mobile (&lt; 1.200 segitiga/prop), dan memiliki pengaturan ModelImporter ringan
///   (isReadable == false, addCollider == false).
/// - Anti-Dirty: 100% read-only (tidak pernah mengubah SerializedObject, .meta, maupun AssetDatabase.SaveAssets()).
/// </summary>
[InitializeOnLoad]
public sealed class GateWeaverAirPropValidator : IPreprocessBuildWithReport
{
    private const string AirPropsFolder = "Assets/Resources/Models/V8_AirProps";
    private const int MaxTrianglesPerProp = 1200;

    private static readonly string[] RequiredPropNames =
    {
        "AirProp_InputBeacon",
        "AirProp_OutputBeaconDrone",
        "AirProp_OutputRelaySpire",
        "AirProp_OutputHoloVault",
        "AirProp_OutputReactorCore",
        "AirProp_OutputTwinSyncCore",
        "AirProp_GateHoverCradle"
    };

    public int callbackOrder => 11;

    static GateWeaverAirPropValidator()
    {
        EditorApplication.delayCall += HandleEditorDelayCall;
    }

    private static void HandleEditorDelayCall()
    {
        if (Application.isPlaying
            || EditorApplication.isPlayingOrWillChangePlaymode
            || EditorApplication.isCompiling
            || EditorApplication.isUpdating)
        {
            return;
        }

        ValidateAllProps(isBuildPreprocess: false);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        ValidateAllProps(isBuildPreprocess: true);
    }

    private static void ValidateAllProps(bool isBuildPreprocess)
    {
        int missingCount = 0;
        int warningCount = 0;
        string firstMissingName = null;

        for (int i = 0; i < RequiredPropNames.Length; i++)
        {
            string propName = RequiredPropNames[i];
            string resolvedPath = ResolvePropAssetPath(propName);
            if (string.IsNullOrEmpty(resolvedPath))
            {
                missingCount++;
                if (firstMissingName == null)
                {
                    firstMissingName = propName;
                }
                continue;
            }

            ModelImporter importer = AssetImporter.GetAtPath(resolvedPath) as ModelImporter;
            if (importer != null)
            {
                if (importer.isReadable)
                {
                    warningCount++;
                    Debug.LogWarning($"[GateWeaverAirPropValidator] '{resolvedPath}' memiliki isReadable=true (disarankan false untuk hemat RAM Android).");
                }
                if (importer.addCollider)
                {
                    warningCount++;
                    Debug.LogWarning($"[GateWeaverAirPropValidator] '{resolvedPath}' memiliki addCollider=true (disarankan false untuk Zero-Physics prop AR).");
                }
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(resolvedPath);
            if (prefab == null)
            {
                missingCount++;
                if (firstMissingName == null)
                {
                    firstMissingName = propName;
                }
                continue;
            }

            int triCount = CountPrefabTriangles(prefab);
            if (triCount <= 0)
            {
                warningCount++;
                Debug.LogWarning($"[GateWeaverAirPropValidator] '{resolvedPath}' tidak memiliki segitiga mesh valid.");
            }
            else if (triCount >= MaxTrianglesPerProp)
            {
                warningCount++;
                Debug.LogWarning($"[GateWeaverAirPropValidator] '{resolvedPath}' memiliki {triCount} segitiga (>= batas mobile {MaxTrianglesPerProp}).");
            }
        }

        if (missingCount > 0)
        {
            string msg = $"[GateWeaverAirPropValidator] {missingCount} prop melayang wajib hilang di '{AirPropsFolder}' (mis. '{firstMissingName}').";
            if (isBuildPreprocess)
            {
                throw new BuildFailedException(msg);
            }

            Debug.LogWarning(msg);
        }
        else if (isBuildPreprocess && warningCount == 0)
        {
            Debug.Log($"[GateWeaverAirPropValidator] Seluruh {RequiredPropNames.Length} prop sci-fi melayang terverifikasi (< {MaxTrianglesPerProp} tris, ringan OpenGLES3).");
        }
    }

    private static string ResolvePropAssetPath(string propName)
    {
        // Mendukung drop-in replacement .fbx maupun default .obj native (keduanya dimuat identik oleh Resources.Load)
        string fbxPath = $"{AirPropsFolder}/{propName}.fbx";
        if (File.Exists(fbxPath))
        {
            return fbxPath;
        }

        string objPath = $"{AirPropsFolder}/{propName}.obj";
        if (File.Exists(objPath))
        {
            return objPath;
        }

        return null;
    }

    private static int CountPrefabTriangles(GameObject prefab)
    {
        MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
        int totalTris = 0;
        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf == null || mf.sharedMesh == null)
            {
                continue;
            }

            Mesh mesh = mf.sharedMesh;
            int subMeshCount = mesh.subMeshCount;
            for (int s = 0; s < subMeshCount; s++)
            {
                totalTris += (int)(mesh.GetIndexCount(s) / 3U);
            }
        }

        return totalTris;
    }
}
#endif
