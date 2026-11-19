#if UNITY_EDITOR && UNITY_ANDROID
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Menyalin pustaka native libc++_shared.so dari Android NDK Unity ke proyek Gradle (:unityLibrary)
/// setelah proyek Gradle dibangkitkan, sehingga MediaPipe (libmediapipe_jni.so) dapat berjalan di Android ARM64
/// tanpa bergantung pada custom mainTemplate.gradle atau perubahan DSL Gradle 8/9 / AGP 8/9.
/// </summary>
public sealed class AndroidLibcppCopier : IPostGenerateGradleAndroidProject
{
    public int callbackOrder => 99;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            Debug.LogWarning($"[AndroidLibcppCopier] Direktori proyek Gradle tidak ditemukan: '{path}'");
            return;
        }

        string ndkRoot = ResolveAndroidNdkRoot();
        if (string.IsNullOrEmpty(ndkRoot) || !Directory.Exists(ndkRoot))
        {
            throw new BuildFailedException(
                "[AndroidLibcppCopier] Direktori Android NDK tidak ditemukan. Pastikan modul Android Build Support (NDK) terpasang di Unity Hub.");
        }

        string sysrootLibDir = ResolveNdkSysrootLibDir(ndkRoot);
        if (string.IsNullOrEmpty(sysrootLibDir) || !Directory.Exists(sysrootLibDir))
        {
            throw new BuildFailedException(
                $"[AndroidLibcppCopier] Folder sysroot/usr/lib tidak ditemukan di dalam NDK: '{ndkRoot}'");
        }

        AndroidArchitecture architectures = PlayerSettings.Android.targetArchitectures;
        bool copiedAny = false;

        if ((architectures & AndroidArchitecture.ARM64) != 0)
        {
            CopyLibcppForAbi(sysrootLibDir, "aarch64-linux-android", path, "arm64-v8a");
            copiedAny = true;
        }

        if ((architectures & AndroidArchitecture.ARMv7) != 0)
        {
            CopyLibcppForAbi(sysrootLibDir, "arm-linux-androideabi", path, "armeabi-v7a");
            copiedAny = true;
        }

        if (!copiedAny)
        {
            CopyLibcppForAbi(sysrootLibDir, "aarch64-linux-android", path, "arm64-v8a");
        }
    }

    private static void CopyLibcppForAbi(string sysrootLibDir, string ndkTriple, string gradleUnityLibraryPath, string androidAbi)
    {
        string sourceFile = Path.Combine(sysrootLibDir, ndkTriple, "libc++_shared.so");
        if (!File.Exists(sourceFile))
        {
            throw new BuildFailedException(
                $"[AndroidLibcppCopier] File libc++_shared.so untuk ABI '{androidAbi}' tidak ditemukan di: '{sourceFile}'");
        }

        string targetDir = Path.Combine(gradleUnityLibraryPath, "src", "main", "jniLibs", androidAbi);
        Directory.CreateDirectory(targetDir);

        string targetFile = Path.Combine(targetDir, "libc++_shared.so");
        File.Copy(sourceFile, targetFile, overwrite: true);
        Debug.Log($"[AndroidLibcppCopier] Berhasil menyalin libc++_shared.so ({androidAbi}) -> {targetFile}");
    }

    private static string ResolveAndroidNdkRoot()
    {
        string configuredNdk = AndroidExternalToolsSettings.ndkRootPath;
        if (!string.IsNullOrEmpty(configuredNdk) && Directory.Exists(configuredNdk))
        {
            return configuredNdk;
        }

        string bundledNdk = Path.Combine(
            EditorApplication.applicationContentsPath,
            "PlaybackEngines",
            "AndroidPlayer",
            "NDK");
        if (Directory.Exists(bundledNdk))
        {
            return bundledNdk;
        }

        string envNdk = Environment.GetEnvironmentVariable("ANDROID_NDK_ROOT");
        if (!string.IsNullOrEmpty(envNdk) && Directory.Exists(envNdk))
        {
            return envNdk;
        }

        string envNdkHome = Environment.GetEnvironmentVariable("ANDROID_NDK_HOME");
        if (!string.IsNullOrEmpty(envNdkHome) && Directory.Exists(envNdkHome))
        {
            return envNdkHome;
        }

        return string.Empty;
    }

    private static string ResolveNdkSysrootLibDir(string ndkRoot)
    {
        string prebuiltRoot = Path.Combine(ndkRoot, "toolchains", "llvm", "prebuilt");
        if (!Directory.Exists(prebuiltRoot))
        {
            return string.Empty;
        }

        string[] hostDirs = Directory.GetDirectories(prebuiltRoot);
        for (int i = 0; i < hostDirs.Length; i++)
        {
            string candidate = Path.Combine(hostDirs[i], "sysroot", "usr", "lib");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return string.Empty;
    }
}
#endif
