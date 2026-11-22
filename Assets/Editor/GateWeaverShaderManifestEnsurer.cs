#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Pengaman anti-stripping shader Android IL2CPP OpenGLES3 (GateWeaver AR v8.0 Tahap 6):
/// - Memastikan material referensi di Assets/Resources/Shaders/ dan daftar ProjectSettings/GraphicsSettings.asset
///   (m_AlwaysIncludedShaders) selalu merujuk pada kedua custom shader GateWeaver.
/// - Anti-Dirty: tidak pernah menyentuh SerializedObject atau AssetDatabase.SaveAssets() bila seluruh manifest
///   sudah valid (0 dirty diff saat domain reload) serta tidak pernah berjalan saat Play Mode aktif.
/// </summary>
[InitializeOnLoad]
public sealed class GateWeaverShaderManifestEnsurer : IPreprocessBuildWithReport
{
    private const string BackdropShaderAssetPath = "Assets/Shaders/ARBackdropDimming.shader";
    private const string HardwareShaderAssetPath = "Assets/Shaders/HologramFresnelHardware.shader";
    private const string ResourcesShadersFolder = "Assets/Resources/Shaders";
    private const string BackdropRefMatPath = "Assets/Resources/Shaders/ARBackdropDimming_Ref.mat";
    private const string HardwareRefMatPath = "Assets/Resources/Shaders/HologramFresnelHardware_Ref.mat";
    private const string GraphicsSettingsAssetPath = "ProjectSettings/GraphicsSettings.asset";

    public int callbackOrder => 10;

    static GateWeaverShaderManifestEnsurer()
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

        EnsureManifest(isBuildPreprocess: false);
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        EnsureManifest(isBuildPreprocess: true);
    }

    private static void EnsureManifest(bool isBuildPreprocess)
    {
        Shader backdropShader = AssetDatabase.LoadAssetAtPath<Shader>(BackdropShaderAssetPath);
        Shader hardwareShader = AssetDatabase.LoadAssetAtPath<Shader>(HardwareShaderAssetPath);

        if (backdropShader == null || hardwareShader == null)
        {
            string missingMsg =
                $"[GateWeaverShaderManifestEnsurer] Custom shader tidak ditemukan (ARBackdropDimming={backdropShader != null}, HologramFresnelHardware={hardwareShader != null}).";
            if (isBuildPreprocess)
            {
                throw new BuildFailedException(missingMsg);
            }

            Debug.LogWarning(missingMsg);
            return;
        }

        bool anyChanged = false;
        anyChanged |= EnsureReferenceMaterial(BackdropRefMatPath, backdropShader, customRenderQueue: 1500);
        anyChanged |= EnsureReferenceMaterial(HardwareRefMatPath, hardwareShader, customRenderQueue: -1);
        anyChanged |= EnsureAlwaysIncludedShaders(backdropShader, hardwareShader);

        if (anyChanged)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[GateWeaverShaderManifestEnsurer] Manifest shader Resources & AlwaysIncludedShaders diperbarui.");
        }
    }

    private static bool EnsureReferenceMaterial(string matAssetPath, Shader targetShader, int customRenderQueue)
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(matAssetPath);
        if (existing != null)
        {
            if (existing.shader == targetShader)
            {
                return false;
            }

            existing.shader = targetShader;
            if (customRenderQueue >= 0)
            {
                existing.renderQueue = customRenderQueue;
            }
            EditorUtility.SetDirty(existing);
            return true;
        }

        EnsureAssetFolderExists(ResourcesShadersFolder);
        Material created = new Material(targetShader)
        {
            name = Path.GetFileNameWithoutExtension(matAssetPath)
        };
        if (customRenderQueue >= 0)
        {
            created.renderQueue = customRenderQueue;
        }

        AssetDatabase.CreateAsset(created, matAssetPath);
        return true;
    }

    private static bool EnsureAlwaysIncludedShaders(Shader backdropShader, Shader hardwareShader)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(GraphicsSettingsAssetPath);
        if (assets == null || assets.Length == 0)
        {
            return false;
        }

        GraphicsSettings graphicsSettings = null;
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is GraphicsSettings gs)
            {
                graphicsSettings = gs;
                break;
            }
        }

        if (graphicsSettings == null)
        {
            return false;
        }

        SerializedObject serializedGraphics = new SerializedObject(graphicsSettings);
        SerializedProperty includedProp = serializedGraphics.FindProperty("m_AlwaysIncludedShaders");
        if (includedProp == null || !includedProp.isArray)
        {
            return false;
        }

        bool hasBackdrop = false;
        bool hasHardware = false;
        int count = includedProp.arraySize;
        for (int i = 0; i < count; i++)
        {
            UnityEngine.Object objRef = includedProp.GetArrayElementAtIndex(i).objectReferenceValue;
            if (objRef == backdropShader)
            {
                hasBackdrop = true;
            }
            else if (objRef == hardwareShader)
            {
                hasHardware = true;
            }
        }

        if (hasBackdrop && hasHardware)
        {
            return false;
        }

        if (!hasBackdrop)
        {
            int idx = includedProp.arraySize;
            includedProp.InsertArrayElementAtIndex(idx);
            includedProp.GetArrayElementAtIndex(idx).objectReferenceValue = backdropShader;
        }

        if (!hasHardware)
        {
            int idx = includedProp.arraySize;
            includedProp.InsertArrayElementAtIndex(idx);
            includedProp.GetArrayElementAtIndex(idx).objectReferenceValue = hardwareShader;
        }

        serializedGraphics.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(graphicsSettings);
        return true;
    }

    private static void EnsureAssetFolderExists(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
        {
            return;
        }

        string normalized = assetFolderPath.Replace('\\', '/');
        string[] parts = normalized.Split('/');
        if (parts.Length < 2 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
        {
            return;
        }

        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}
#endif
