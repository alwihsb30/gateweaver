using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Toolkit Geometri &amp; Material Prosedural GateWeaver AR v8.5
/// ("Smoked Optical Glass + Brushed Titanium" precision hardware):
/// - Mesh Bevel Prosedural (ChamferedBox potongan bevel 7.2% dengan split/flat normal CNC,
///   BeveledCylinder 16 sisi ber-bevel atas/bawah, serta IrisBlade tipis untuk Output Vault).
/// - Studio Softbox Cubemap Prosedural (64x64) untuk refleksi studio netral agar bodi logam
///   Brushed Titanium selalu tampak terang bervolume di OpenGLES3 Android.
/// - Disiplin Shared Material 60-30-10 (di-cache static sekali, 100% Zero-GC di runtime loop).
/// </summary>
public static class GwIndustrial3DKit
{
    // =========================================================================
    // PALET 3D INDUSTRIAL 60-30-10 (SMOKED OPTICAL GLASS + BRUSHED TITANIUM)
    // =========================================================================

    /// <summary>#C8D0DA - 60% Brushed Titanium untuk cangkang utama &amp; bodi aktuator.</summary>
    public static readonly Color ColorBrushedTitanium = new Color(0xC8 / 255f, 0xD0 / 255f, 0xDA / 255f, 1.00f);

    /// <summary>#848E9C - Varian Brushed Titanium lebih tenang/gelap khusus bingkai meja agar tidak mengalahkan fokus prop.</summary>
    public static readonly Color ColorStageFrameTitanium = new Color(0x84 / 255f, 0x8E / 255f, 0x9C / 255f, 1.00f);

    /// <summary>#1C2029 - 30% Anodized Graphite matte gelap untuk ceruk, plinth, dok, soket, &amp; bilah iris.</summary>
    public static readonly Color ColorAnodizedGraphite = new Color(0x1C / 255f, 0x20 / 255f, 0x29 / 255f, 1.00f);

    /// <summary>#10131B alpha 0.44f - Pelat meja kerja Smoked Optical Tray transparan bersih.</summary>
    public static readonly Color ColorSmokedOpticalTray = new Color(0x10 / 255f, 0x13 / 255f, 0x1B / 255f, 0.44f);

    /// <summary>#10131B alpha 0.68f - Jendela inti kaca asap gelap di tengah kartrid IC gerbang logika.</summary>
    public static readonly Color ColorSmokedGlassCore = new Color(0x10 / 255f, 0x13 / 255f, 0x1B / 255f, 0.68f);

    /// <summary>#F97316 - Aksen mikro ujung tuas sakelar mekanis (SignalOrange).</summary>
    public static readonly Color ColorSignalOrange = GwThemeTokens.AccentSwitchOrange;

    /// <summary>#38BDF8 - Sinyal 1 / HIGH / LED aktif / Kabel 1 (SignalTeal).</summary>
    public static readonly Color ColorSignalTeal = GwThemeTokens.SignalHighTeal;

    /// <summary>#475569 - Sinyal 0 / LOW / LED diam / Kabel 0 (SignalSlate).</summary>
    public static readonly Color ColorSignalSlate = new Color(0x47 / 255f, 0x55 / 255f, 0x69 / 255f, 1.00f);

    /// <summary>#EF4444 - Titik LED terkunci Output Vault saat 0 / Error (SignalRed).</summary>
    public static readonly Color ColorSignalRed = GwThemeTokens.StatusErrorRed;

    /// <summary>#10B981 - Inti cahaya Output Vault saat 1 / Terbuka / Lulus (PassEmerald).</summary>
    public static readonly Color ColorPassEmerald = GwThemeTokens.StatusPassEmerald;

    private static readonly int ColorPropId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorPropId = Shader.PropertyToID("_EmissionColor");
    private static readonly int MetallicPropId = Shader.PropertyToID("_Metallic");
    private static readonly int GlossinessPropId = Shader.PropertyToID("_Glossiness");
    private static readonly int RimColorPropId = Shader.PropertyToID("_RimColor");
    private static readonly int RimPowerPropId = Shader.PropertyToID("_RimPower");
    private static readonly int RimIntensityPropId = Shader.PropertyToID("_RimIntensity");
    private static readonly int WrapDiffusePropId = Shader.PropertyToID("_WrapDiffuse");
    private static readonly int RimFillBiasPropId = Shader.PropertyToID("_RimFillBias");
    private static readonly int GwStudioCubemapPropId = Shader.PropertyToID("_GW_StudioSoftboxCube");

    private const int StudioCubemapSize = 64;
    private const int SmokedTrayRenderQueue = 2975;
    private const int SmokedCoreGlassRenderQueue = 2962;

    // Cache statis (dibangun sekali, Zero-GC di Update)
    private static Mesh cachedChamferedBoxMesh;
    private static Mesh cachedBeveledCylinderMesh;
    private static Mesh cachedIrisBladeMesh;
    private static Cubemap cachedStudioSoftboxCubemap;

    private static Material cachedMatBrushedTitanium;
    private static Material cachedMatStageFrameTitanium;
    private static Material cachedMatAnodizedGraphite;
    private static Material cachedMatSmokedOpticalTray;
    private static Material cachedMatSmokedGlassCore;
    private static Material cachedMatSignalOrange;
    private static Material cachedMatSignalTeal;
    private static Material cachedMatSignalSlate;
    private static Material cachedMatSignalRed;
    private static Material cachedMatPassEmerald;

    // Snapshot refleksi scene agar Mode Kartu Vuforia v5 dipulihkan 100% utuh
    private static bool hasSavedReflectionState = false;
    private static DefaultReflectionMode savedReflectionMode = DefaultReflectionMode.Skybox;
    private static Texture savedCustomReflectionTexture = null;
    private static float savedReflectionIntensity = 1.0f;

    // =========================================================================
    // AKSESOR SHARED MATERIAL 60-30-10 (LAZY CACHED, ZERO-GC)
    // =========================================================================

    public static Material Mat_BrushedTitanium
    {
        get
        {
            if (cachedMatBrushedTitanium == null)
            {
                // Metalik sedang (0.34) + smoothness terkontrol (0.62) + studio fill halus agar terang bervolume di AR
                cachedMatBrushedTitanium = CreateHardwareMaterial(
                    "GW_Mat_BrushedTitanium",
                    ColorBrushedTitanium,
                    metallic: 0.34f,
                    smoothness: 0.62f,
                    emissionColor: new Color(0.08f, 0.09f, 0.11f, 1.0f),
                    wrapDiffuse: 0.32f,
                    rimColor: new Color(0.54f, 0.58f, 0.64f, 1.0f),
                    rimIntensity: 0.22f,
                    rimFillBias: 0.04f);
            }
            return cachedMatBrushedTitanium;
        }
    }

    public static Material Mat_StageFrameTitanium
    {
        get
        {
            if (cachedMatStageFrameTitanium == null)
            {
                // Varian bingkai meja titanium lebih tenang/gelap (#848E9C) & smoothness lebih rendah (0.42)
                cachedMatStageFrameTitanium = CreateHardwareMaterial(
                    "GW_Mat_StageFrameTitanium",
                    ColorStageFrameTitanium,
                    metallic: 0.28f,
                    smoothness: 0.42f,
                    emissionColor: new Color(0.015f, 0.017f, 0.021f, 1.0f),
                    wrapDiffuse: 0.26f,
                    rimColor: new Color(0.30f, 0.34f, 0.40f, 1.0f),
                    rimIntensity: 0.10f,
                    rimFillBias: 0.015f);
            }
            return cachedMatStageFrameTitanium;
        }
    }

    public static Material Mat_AnodizedGraphite
    {
        get
        {
            if (cachedMatAnodizedGraphite == null)
            {
                // Matte gelap murni (#1C2029) dengan 0% rim cyan bawaan shader agar bantalan dok bebas semburat biru/teal
                cachedMatAnodizedGraphite = CreateHardwareMaterial(
                    "GW_Mat_AnodizedGraphite",
                    ColorAnodizedGraphite,
                    metallic: 0.18f,
                    smoothness: 0.28f,
                    emissionColor: Color.black,
                    wrapDiffuse: 0.22f,
                    rimColor: Color.black,
                    rimIntensity: 0.0f,
                    rimFillBias: 0.0f);
            }
            return cachedMatAnodizedGraphite;
        }
    }

    public static Material Mat_SmokedOpticalTray
    {
        get
        {
            if (cachedMatSmokedOpticalTray == null)
            {
                cachedMatSmokedOpticalTray = CreateTransparentSpritesMaterial(
                    "GW_Mat_SmokedOpticalTray",
                    ColorSmokedOpticalTray,
                    SmokedTrayRenderQueue);
            }
            return cachedMatSmokedOpticalTray;
        }
    }

    public static Material Mat_SmokedGlassCore
    {
        get
        {
            if (cachedMatSmokedGlassCore == null)
            {
                cachedMatSmokedGlassCore = CreateTransparentSpritesMaterial(
                    "GW_Mat_SmokedGlassCore",
                    ColorSmokedGlassCore,
                    SmokedCoreGlassRenderQueue);
            }
            return cachedMatSmokedGlassCore;
        }
    }

    public static Material Mat_SignalOrange
    {
        get
        {
            if (cachedMatSignalOrange == null)
            {
                cachedMatSignalOrange = CreateHardwareMaterial(
                    "GW_Mat_SignalOrange",
                    ColorSignalOrange,
                    metallic: 0.16f,
                    smoothness: 0.68f,
                    emissionColor: ColorSignalOrange * 0.42f,
                    wrapDiffuse: 0.30f);
            }
            return cachedMatSignalOrange;
        }
    }

    public static Material Mat_SignalTeal
    {
        get
        {
            if (cachedMatSignalTeal == null)
            {
                cachedMatSignalTeal = CreateHardwareMaterial(
                    "GW_Mat_SignalTeal",
                    ColorSignalTeal,
                    metallic: 0.18f,
                    smoothness: 0.76f,
                    emissionColor: ColorSignalTeal * 0.62f,
                    wrapDiffuse: 0.28f);
            }
            return cachedMatSignalTeal;
        }
    }

    public static Material Mat_SignalSlate
    {
        get
        {
            if (cachedMatSignalSlate == null)
            {
                cachedMatSignalSlate = CreateHardwareMaterial(
                    "GW_Mat_SignalSlate",
                    ColorSignalSlate,
                    metallic: 0.22f,
                    smoothness: 0.46f,
                    emissionColor: ColorSignalSlate * 0.12f,
                    wrapDiffuse: 0.28f);
            }
            return cachedMatSignalSlate;
        }
    }

    public static Material Mat_SignalRed
    {
        get
        {
            if (cachedMatSignalRed == null)
            {
                cachedMatSignalRed = CreateHardwareMaterial(
                    "GW_Mat_SignalRed",
                    ColorSignalRed,
                    metallic: 0.18f,
                    smoothness: 0.72f,
                    emissionColor: ColorSignalRed * 0.58f,
                    wrapDiffuse: 0.28f);
            }
            return cachedMatSignalRed;
        }
    }

    public static Material Mat_PassEmerald
    {
        get
        {
            if (cachedMatPassEmerald == null)
            {
                cachedMatPassEmerald = CreateHardwareMaterial(
                    "GW_Mat_PassEmerald",
                    ColorPassEmerald,
                    metallic: 0.20f,
                    smoothness: 0.78f,
                    emissionColor: ColorPassEmerald * 0.68f,
                    wrapDiffuse: 0.28f);
            }
            return cachedMatPassEmerald;
        }
    }

    public static void EnsureInitialized()
    {
        _ = GetStudioSoftboxCubemap();
        _ = GetChamferedBoxMesh();
        _ = GetBeveledCylinderMesh();
        _ = GetIrisBladeMesh();
        _ = Mat_BrushedTitanium;
        _ = Mat_StageFrameTitanium;
        _ = Mat_AnodizedGraphite;
        _ = Mat_SmokedOpticalTray;
        _ = Mat_SmokedGlassCore;
        _ = Mat_SignalOrange;
        _ = Mat_SignalTeal;
        _ = Mat_SignalSlate;
        _ = Mat_SignalRed;
        _ = Mat_PassEmerald;
    }

    /// <summary>
    /// Memasang Studio Softbox Cubemap 64x64 ke environment refleksi selama Mode Udara aktif
    /// agar permukaan logam Brushed Titanium menangkap highlight studio lembut di OpenGLES3.
    /// </summary>
    public static void ApplyStudioReflectionEnvironment()
    {
        EnsureInitialized();
        Cubemap cube = GetStudioSoftboxCubemap();
        if (cube == null)
        {
            return;
        }

        if (!hasSavedReflectionState)
        {
            savedReflectionMode = RenderSettings.defaultReflectionMode;
            savedCustomReflectionTexture = RenderSettings.customReflectionTexture;
            savedReflectionIntensity = RenderSettings.reflectionIntensity;
            hasSavedReflectionState = true;
        }

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = cube;
        RenderSettings.reflectionIntensity = 1.0f;
        Shader.SetGlobalTexture(GwStudioCubemapPropId, cube);
    }

    /// <summary>
    /// Memulihkan 100% pengaturan refleksi scene bawaan saat kembali ke Mode Kartu Vuforia v5.
    /// </summary>
    public static void RestoreSceneReflectionEnvironment()
    {
        if (!hasSavedReflectionState)
        {
            return;
        }

        RenderSettings.defaultReflectionMode = savedReflectionMode;
        RenderSettings.customReflectionTexture = savedCustomReflectionTexture;
        RenderSettings.reflectionIntensity = savedReflectionIntensity;
        hasSavedReflectionState = false;
    }

    /// <summary>
    /// Membuat instance material aksen reaktif terdedikasi (misal untuk strip LED per-node yang berubah 0/1).
    /// </summary>
    public static Material CreateDedicatedReactiveMaterial(string matName, Color initialBase, Color initialEmission, float metallic = 0.20f, float smoothness = 0.74f)
    {
        return CreateHardwareMaterial(matName, initialBase, metallic, smoothness, initialEmission, wrapDiffuse: 0.28f, hideAndDontSave: false);
    }

    public static void SetMaterialColorAndEmission(Material mat, Color baseColor, Color emissionColor)
    {
        if (mat == null)
        {
            return;
        }

        if (mat.HasProperty(ColorPropId))
        {
            mat.SetColor(ColorPropId, baseColor);
        }
        else
        {
            mat.color = baseColor;
        }

        if (mat.HasProperty(EmissionColorPropId))
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor(EmissionColorPropId, emissionColor);
        }
    }

    private static Material CreateHardwareMaterial(
        string matName,
        Color baseColor,
        float metallic,
        float smoothness,
        Color emissionColor,
        float wrapDiffuse = 0.30f,
        bool hideAndDontSave = true,
        Color? rimColor = null,
        float rimIntensity = 0.12f,
        float rimFillBias = 0.02f)
    {
        Shader shader = AirWeavingLightingAndShaderRig.ResolveHardwareFresnelShader();
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("Standard");
        }
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("Legacy Shaders/Diffuse");
        }
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("Sprites/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader)
        {
            name = matName,
            renderQueue = -1
        };
        if (hideAndDontSave)
        {
            mat.hideFlags = HideFlags.HideAndDontSave;
        }

        SetMaterialColorAndEmission(mat, baseColor, emissionColor);
        if (mat.HasProperty(MetallicPropId))
        {
            mat.SetFloat(MetallicPropId, metallic);
        }
        if (mat.HasProperty(GlossinessPropId))
        {
            mat.SetFloat(GlossinessPropId, smoothness);
        }
        if (mat.HasProperty(WrapDiffusePropId))
        {
            mat.SetFloat(WrapDiffusePropId, wrapDiffuse);
        }
        if (mat.HasProperty(RimColorPropId))
        {
            mat.SetColor(RimColorPropId, rimColor ?? new Color(0.38f, 0.42f, 0.48f, 1.0f));
        }
        if (mat.HasProperty(RimPowerPropId))
        {
            mat.SetFloat(RimPowerPropId, 3.0f);
        }
        if (mat.HasProperty(RimIntensityPropId))
        {
            mat.SetFloat(RimIntensityPropId, rimIntensity);
        }
        if (mat.HasProperty(RimFillBiasPropId))
        {
            mat.SetFloat(RimFillBiasPropId, rimFillBias);
        }
        return mat;
    }

    private static Material CreateTransparentSpritesMaterial(string matName, Color tint, int renderQueue)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null || !shader.isSupported)
        {
            shader = Shader.Find("UI/Default");
        }
        if (shader == null)
        {
            return null;
        }

        Material mat = new Material(shader)
        {
            name = matName,
            color = tint,
            renderQueue = renderQueue,
            hideFlags = HideFlags.HideAndDontSave
        };
        return mat;
    }

    // =========================================================================
    // STUDIO SOFTBOX CUBEMAP PROSEDURAL 64x64 (CACHED STATIC, ZERO-GC)
    // =========================================================================

    public static Cubemap GetStudioSoftboxCubemap()
    {
        if (cachedStudioSoftboxCubemap != null)
        {
            return cachedStudioSoftboxCubemap;
        }

        const int size = StudioCubemapSize;
        Cubemap cube = new Cubemap(size, TextureFormat.RGBA32, mipChain: false)
        {
            name = "GW_V85_StudioSoftboxCubemap64",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] facePixels = new Color[size * size];

        // Arah 2 panel softbox studio atas (Key Softbox di kiri-atas-depan, Fill Softbox di kanan-atas-belakang)
        Vector3 softboxA = new Vector3(-0.42f, 0.80f, -0.42f).normalized;
        Vector3 softboxB = new Vector3(0.48f, 0.74f, 0.46f).normalized;

        CubemapFace[] faces =
        {
            CubemapFace.PositiveX,
            CubemapFace.NegativeX,
            CubemapFace.PositiveY,
            CubemapFace.NegativeY,
            CubemapFace.PositiveZ,
            CubemapFace.NegativeZ
        };

        for (int f = 0; f < faces.Length; f++)
        {
            CubemapFace face = faces[f];
            for (int y = 0; y < size; y++)
            {
                float v = (((y + 0.5f) / size) * 2f) - 1f;
                int rowOffset = y * size;
                for (int x = 0; x < size; x++)
                {
                    float u = (((x + 0.5f) / size) * 2f) - 1f;
                    Vector3 dir = CubemapTexelToDirection(face, u, v);

                    // 1. Gradien studio netral terang (bawah abu medium -> ekuator perak -> zenit putih lembut)
                    float upT = Mathf.Clamp01((dir.y * 0.5f) + 0.5f);
                    Color baseGrad = Color.Lerp(
                        new Color(0.36f, 0.40f, 0.46f, 1f),
                        new Color(0.84f, 0.88f, 0.93f, 1f),
                        Mathf.SmoothStep(0f, 1f, upT));

                    // 2. Pita horizon studio (memantulkan garis kilau CNC pada bevel vertikal)
                    float horizonBand = Mathf.Exp(-18.0f * (dir.y - 0.12f) * (dir.y - 0.12f));
                    baseGrad = Color.Lerp(baseGrad, new Color(0.94f, 0.96f, 0.99f, 1f), horizonBand * 0.38f);

                    // 3. Dua pita Softbox Putih di hemisfer atas
                    float dotA = Mathf.Max(0f, Vector3.Dot(dir, softboxA));
                    float dotB = Mathf.Max(0f, Vector3.Dot(dir, softboxB));
                    float boxA = Mathf.SmoothStep(0.68f, 0.92f, dotA);
                    float boxB = Mathf.SmoothStep(0.72f, 0.94f, dotB) * 0.82f;
                    float softboxBoost = Mathf.Clamp01(boxA + boxB);

                    Color finalCol = Color.Lerp(baseGrad, Color.white, softboxBoost);
                    finalCol.a = 1f;
                    facePixels[rowOffset + x] = finalCol;
                }
            }

            cube.SetPixels(facePixels, face);
        }

        cube.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        cachedStudioSoftboxCubemap = cube;
        return cachedStudioSoftboxCubemap;
    }

    private static Vector3 CubemapTexelToDirection(CubemapFace face, float u, float v)
    {
        Vector3 dir;
        switch (face)
        {
            case CubemapFace.PositiveX: dir = new Vector3(1f, -v, -u); break;
            case CubemapFace.NegativeX: dir = new Vector3(-1f, -v, u); break;
            case CubemapFace.PositiveY: dir = new Vector3(u, 1f, v); break;
            case CubemapFace.NegativeY: dir = new Vector3(u, -1f, -v); break;
            case CubemapFace.PositiveZ: dir = new Vector3(u, -v, 1f); break;
            default: dir = new Vector3(-u, -v, -1f); break;
        }
        return dir.normalized;
    }

    // =========================================================================
    // GENERATOR MESH BEVEL PROSEDURAL (SPLIT/FLAT NORMALS CNC, CACHED STATIC)
    // =========================================================================

    /// <summary>
    /// Mesh ChamferedBox unit (1x1x1, berpusat di 0,0,0) dengan potongan bevel 7.2% di ke-12 rusuk &amp; 8 sudut,
    /// menggunakan split/flat normal pada tiap bidang bevel agar menangkap kilau cahaya CNC (44 tris, 96 verts).
    /// </summary>
    public static Mesh GetChamferedBoxMesh()
    {
        if (cachedChamferedBoxMesh == null)
        {
            cachedChamferedBoxMesh = BuildChamferedBoxMesh("GW_V85_ChamferedBoxMesh", Vector3.one, chamferFraction: 0.072f);
            cachedChamferedBoxMesh.hideFlags = HideFlags.HideAndDontSave;
        }
        return cachedChamferedBoxMesh;
    }

    /// <summary>
    /// Mesh BeveledCylinder unit (16 sisi, radius=0.5, tinggi=1.0, y in [-0.5..+0.5]) dengan cincin bevel
    /// atas &amp; bawah 7.5% ber-normal split agar tepi silinder memantulkan highlight logam tajam (128 tris).
    /// </summary>
    public static Mesh GetBeveledCylinderMesh()
    {
        if (cachedBeveledCylinderMesh == null)
        {
            cachedBeveledCylinderMesh = BuildBeveledCylinderMesh("GW_V85_BeveledCylinderMesh", segments: 16, radius: 0.5f, height: 1.0f, bevelFraction: 0.075f);
            cachedBeveledCylinderMesh.hideFlags = HideFlags.HideAndDontSave;
        }
        return cachedBeveledCylinderMesh;
    }

    /// <summary>
    /// Mesh IrisBlade tipis ber-bevel untuk 6 bilah kunci mekanis Output Vault (20 tris per bilah).
    /// </summary>
    public static Mesh GetIrisBladeMesh()
    {
        if (cachedIrisBladeMesh == null)
        {
            cachedIrisBladeMesh = BuildIrisBladeMesh("GW_V85_IrisBladeMesh");
            cachedIrisBladeMesh.hideFlags = HideFlags.HideAndDontSave;
        }
        return cachedIrisBladeMesh;
    }

    public static Mesh BuildChamferedBoxMesh(string meshName, Vector3 size, float chamferFraction = 0.072f)
    {
        float hx = Mathf.Max(0.005f, size.x * 0.5f);
        float hy = Mathf.Max(0.005f, size.y * 0.5f);
        float hz = Mathf.Max(0.005f, size.z * 0.5f);
        float minHalf = Mathf.Min(hx, Mathf.Min(hy, hz));
        float b = Mathf.Clamp( chamferFraction * (minHalf * 2f), minHalf * 0.08f, minHalf * 0.36f);

        float ix = hx - b;
        float iy = hy - b;
        float iz = hz - b;

        // 6 bidang utama (24v, 12t) + 12 bidang bevel rusuk (48v, 24t) + 8 segitiga bevel sudut (24v, 8t) = 96v, 44t
        List<Vector3> verts = new List<Vector3>(96);
        List<Vector3> norms = new List<Vector3>(96);
        List<int> tris = new List<int>(132);

        // 1. 6 Bidang Utama
        // +Y & -Y
        AddFlatQuad(verts, norms, tris,
            new Vector3(-ix, hy, -iz), new Vector3(-ix, hy, iz), new Vector3(ix, hy, iz), new Vector3(ix, hy, -iz),
            Vector3.up);
        AddFlatQuad(verts, norms, tris,
            new Vector3(-ix, -hy, iz), new Vector3(-ix, -hy, -iz), new Vector3(ix, -hy, -iz), new Vector3(ix, -hy, iz),
            Vector3.down);

        // +X & -X
        AddFlatQuad(verts, norms, tris,
            new Vector3(hx, -iy, -iz), new Vector3(hx, iy, -iz), new Vector3(hx, iy, iz), new Vector3(hx, -iy, iz),
            Vector3.right);
        AddFlatQuad(verts, norms, tris,
            new Vector3(-hx, -iy, iz), new Vector3(-hx, iy, iz), new Vector3(-hx, iy, -iz), new Vector3(-hx, -iy, -iz),
            Vector3.left);

        // +Z & -Z
        AddFlatQuad(verts, norms, tris,
            new Vector3(ix, -iy, hz), new Vector3(ix, iy, hz), new Vector3(-ix, iy, hz), new Vector3(-ix, -iy, hz),
            Vector3.forward);
        AddFlatQuad(verts, norms, tris,
            new Vector3(-ix, -iy, -hz), new Vector3(-ix, iy, -hz), new Vector3(ix, iy, -hz), new Vector3(ix, -iy, -hz),
            Vector3.back);

        // 2. 12 Bidang Bevel Rusuk (Split Flat Normals 45 derajat)
        int[] signs = { -1, 1 };
        for (int a = 0; a < 2; a++)
        {
            float s1 = signs[a];
            for (int c = 0; c < 2; c++)
            {
                float s2 = signs[c];

                // 4 rusuk sejajar sumbu Z (antara bidang X dan bidang Y)
                Vector3 nXY = new Vector3(s1, s2, 0f).normalized;
                AddFlatQuad(verts, norms, tris,
                    new Vector3(s1 * hx, s2 * iy, -iz),
                    new Vector3(s1 * hx, s2 * iy, iz),
                    new Vector3(s1 * ix, s2 * hy, iz),
                    new Vector3(s1 * ix, s2 * hy, -iz),
                    nXY);

                // 4 rusuk sejajar sumbu X (antara bidang Y dan bidang Z)
                Vector3 nYZ = new Vector3(0f, s1, s2).normalized;
                AddFlatQuad(verts, norms, tris,
                    new Vector3(-ix, s1 * hy, s2 * iz),
                    new Vector3(ix, s1 * hy, s2 * iz),
                    new Vector3(ix, s1 * iy, s2 * hz),
                    new Vector3(-ix, s1 * iy, s2 * hz),
                    nYZ);

                // 4 rusuk sejajar sumbu Y (antara bidang X dan bidang Z)
                Vector3 nXZ = new Vector3(s1, 0f, s2).normalized;
                AddFlatQuad(verts, norms, tris,
                    new Vector3(s1 * hx, -iy, s2 * iz),
                    new Vector3(s1 * hx, iy, s2 * iz),
                    new Vector3(s1 * ix, iy, s2 * hz),
                    new Vector3(s1 * ix, -iy, s2 * hz),
                    nXZ);
            }
        }

        // 3. 8 Segitiga Bevel Sudut
        for (int xi = 0; xi < 2; xi++)
        {
            float sx = signs[xi];
            for (int yi = 0; yi < 2; yi++)
            {
                float sy = signs[yi];
                for (int zi = 0; zi < 2; zi++)
                {
                    float sz = signs[zi];
                    Vector3 nCorner = new Vector3(sx, sy, sz).normalized;
                    Vector3 px = new Vector3(sx * hx, sy * iy, sz * iz);
                    Vector3 py = new Vector3(sx * ix, sy * hy, sz * iz);
                    Vector3 pz = new Vector3(sx * ix, sy * iy, sz * hz);
                    AddFlatTri(verts, norms, tris, px, py, pz, nCorner);
                }
            }
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: true);
        return mesh;
    }

    public static Mesh BuildBeveledCylinderMesh(string meshName, int segments, float radius, float height, float bevelFraction = 0.075f)
    {
        int segs = Mathf.Clamp(segments, 8, 24);
        float halfH = height * 0.5f;
        float bevR = Mathf.Clamp(radius * (1f - (bevelFraction * 1.6f)), radius * 0.65f, radius * 0.96f);
        float bevY = Mathf.Clamp(halfH - (height * bevelFraction), halfH * 0.60f, halfH * 0.96f);

        List<Vector3> verts = new List<Vector3>(segs * 10);
        List<Vector3> norms = new List<Vector3>(segs * 10);
        List<int> tris = new List<int>(segs * 24);

        float invSegs = (Mathf.PI * 2f) / segs;

        for (int i = 0; i < segs; i++)
        {
            int next = (i + 1) % segs;
            float a0 = i * invSegs;
            float a1 = next * invSegs;

            float c0 = Mathf.Cos(a0);
            float s0 = Mathf.Sin(a0);
            float c1 = Mathf.Cos(a1);
            float s1 = Mathf.Sin(a1);

            // 1. Tutup Atas (+Y)
            Vector3 topCenter = new Vector3(0f, halfH, 0f);
            Vector3 topRim0 = new Vector3(c0 * bevR, halfH, s0 * bevR);
            Vector3 topRim1 = new Vector3(c1 * bevR, halfH, s1 * bevR);
            AddFlatTri(verts, norms, tris, topCenter, topRim1, topRim0, Vector3.up);

            // 2. Cincin Bevel Atas (Split Normal miring 45 derajat agar berkilau CNC)
            Vector3 upperOuter0 = new Vector3(c0 * radius, bevY, s0 * radius);
            Vector3 upperOuter1 = new Vector3(c1 * radius, bevY, s1 * radius);
            Vector3 midRadial = new Vector3((c0 + c1) * 0.5f, 0f, (s0 + s1) * 0.5f).normalized;
            Vector3 topBevelNormal = (midRadial + Vector3.up).normalized;
            AddFlatQuad(verts, norms, tris, topRim0, topRim1, upperOuter1, upperOuter0, topBevelNormal);

            // 3. Dinding Silinder Utama (Smooth Radial Normal)
            Vector3 lowerOuter0 = new Vector3(c0 * radius, -bevY, s0 * radius);
            Vector3 lowerOuter1 = new Vector3(c1 * radius, -bevY, s1 * radius);
            Vector3 n0 = new Vector3(c0, 0f, s0);
            Vector3 n1 = new Vector3(c1, 0f, s1);
            AddSmoothBarrelQuad(verts, norms, tris, lowerOuter0, upperOuter0, upperOuter1, lowerOuter1, n0, n1);

            // 4. Cincin Bevel Bawah
            Vector3 botRim0 = new Vector3(c0 * bevR, -halfH, s0 * bevR);
            Vector3 botRim1 = new Vector3(c1 * bevR, -halfH, s1 * bevR);
            Vector3 botBevelNormal = (midRadial + Vector3.down).normalized;
            AddFlatQuad(verts, norms, tris, lowerOuter0, lowerOuter1, botRim1, botRim0, botBevelNormal);

            // 5. Tutup Bawah (-Y)
            Vector3 botCenter = new Vector3(0f, -halfH, 0f);
            AddFlatTri(verts, norms, tris, botCenter, botRim0, botRim1, Vector3.down);
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: true);
        return mesh;
    }

    /// <summary>
    /// Membangun bilah iris mekanis (sektor baji 60 derajat ber-bevel) dengan poros engsel di pangkal luar (0,0,0)
    /// dan ujung penutup meruncing ke arah pusat (+Z lokal) sehingga 6 bilah menutup rapat saat 0 dan
    /// berputar membuka aperture lingkaran saat 1.
    /// </summary>
    private static Mesh BuildIrisBladeMesh(string meshName)
    {
        const float halfH = 0.0085f;
        const float tipBevelH = 0.0035f;

        // 5 titik poligon bilah iris di bidang XZ (origin 0,0 = poros engsel tepi luar; +Z = mengarah ke pusat brankas)
        Vector3 p0 = new Vector2ToXZ(-0.044f, 0.000f).ToVec(0f); // Pangkal kiri luar
        Vector3 p1 = new Vector2ToXZ(0.044f, 0.000f).ToVec(0f);  // Pangkal kanan luar
        Vector3 p2 = new Vector2ToXZ(0.052f, 0.068f).ToVec(0f);  // Bahu kanan tengah
        Vector3 p3 = new Vector2ToXZ(0.002f, 0.112f).ToVec(0f);  // Ujung lancip pusat
        Vector3 p4 = new Vector2ToXZ(-0.036f, 0.078f).ToVec(0f); // Sisi bevel dalam kiri

        Vector3 t0 = new Vector3(p0.x, halfH, p0.z);
        Vector3 t1 = new Vector3(p1.x, halfH, p1.z);
        Vector3 t2 = new Vector3(p2.x, halfH, p2.z);
        Vector3 t3 = new Vector3(p3.x, tipBevelH, p3.z); // Ujung depan di-bevel miring
        Vector3 t4 = new Vector3(p4.x, tipBevelH, p4.z);

        Vector3 b0 = new Vector3(p0.x, -halfH, p0.z);
        Vector3 b1 = new Vector3(p1.x, -halfH, p1.z);
        Vector3 b2 = new Vector3(p2.x, -halfH, p2.z);
        Vector3 b3 = new Vector3(p3.x, -halfH, p3.z);
        Vector3 b4 = new Vector3(p4.x, -halfH, p4.z);

        List<Vector3> verts = new List<Vector3>(48);
        List<Vector3> norms = new List<Vector3>(48);
        List<int> tris = new List<int>(60);

        // Permukaan atas utama & bidang bevel ujung
        AddFlatTri(verts, norms, tris, t0, t2, t1, Vector3.up);
        Vector3 bevelNorm = Vector3.Cross(t3 - t0, t2 - t0).normalized;
        if (bevelNorm.y < 0f) bevelNorm = -bevelNorm;
        AddFlatTri(verts, norms, tris, t0, t4, t3, bevelNorm);
        AddFlatTri(verts, norms, tris, t0, t3, t2, bevelNorm);

        // Permukaan bawah
        AddFlatTri(verts, norms, tris, b0, b1, b2, Vector3.down);
        AddFlatTri(verts, norms, tris, b0, b2, b3, Vector3.down);
        AddFlatTri(verts, norms, tris, b0, b3, b4, Vector3.down);

        // 5 dinding tepi keliling bilah
        AddPerimeterWallQuad(verts, norms, tris, b0, b1, t1, t0);
        AddPerimeterWallQuad(verts, norms, tris, b1, b2, t2, t1);
        AddPerimeterWallQuad(verts, norms, tris, b2, b3, t3, t2);
        AddPerimeterWallQuad(verts, norms, tris, b3, b4, t4, t3);
        AddPerimeterWallQuad(verts, norms, tris, b4, b0, t0, t4);

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetIndices(tris, MeshTopology.Triangles, 0, calculateBounds: true);
        return mesh;
    }

    private readonly struct Vector2ToXZ
    {
        private readonly float x;
        private readonly float z;

        public Vector2ToXZ(float xVal, float zVal)
        {
            x = xVal;
            z = zVal;
        }

        public Vector3 ToVec(float yVal)
        {
            return new Vector3(x, yVal, z);
        }
    }

    public static void AddFlatTri(
        List<Vector3> verts,
        List<Vector3> norms,
        List<int> tris,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 outwardNormal)
    {
        int baseIdx = verts.Count;
        Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
        bool flip = Vector3.Dot(cross, outwardNormal) < 0f;

        verts.Add(v0);
        verts.Add(flip ? v2 : v1);
        verts.Add(flip ? v1 : v2);

        norms.Add(outwardNormal);
        norms.Add(outwardNormal);
        norms.Add(outwardNormal);

        tris.Add(baseIdx);
        tris.Add(baseIdx + 1);
        tris.Add(baseIdx + 2);
    }

    public static void AddFlatQuad(
        List<Vector3> verts,
        List<Vector3> norms,
        List<int> tris,
        Vector3 v0,
        Vector3 v1,
        Vector3 v2,
        Vector3 v3,
        Vector3 outwardNormal)
    {
        int baseIdx = verts.Count;
        Vector3 cross = Vector3.Cross(v1 - v0, v2 - v0);
        bool flip = Vector3.Dot(cross, outwardNormal) < 0f;

        verts.Add(v0);
        verts.Add(v1);
        verts.Add(v2);
        verts.Add(v3);

        norms.Add(outwardNormal);
        norms.Add(outwardNormal);
        norms.Add(outwardNormal);
        norms.Add(outwardNormal);

        if (!flip)
        {
            tris.Add(baseIdx);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 3);
        }
        else
        {
            tris.Add(baseIdx);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx);
            tris.Add(baseIdx + 3);
            tris.Add(baseIdx + 2);
        }
    }

    private static void AddSmoothBarrelQuad(
        List<Vector3> verts,
        List<Vector3> norms,
        List<int> tris,
        Vector3 bot0,
        Vector3 top0,
        Vector3 top1,
        Vector3 bot1,
        Vector3 n0,
        Vector3 n1)
    {
        int baseIdx = verts.Count;
        Vector3 midOut = (n0 + n1).normalized;
        Vector3 cross = Vector3.Cross(top0 - bot0, top1 - bot0);
        bool flip = Vector3.Dot(cross, midOut) < 0f;

        verts.Add(bot0);
        verts.Add(top0);
        verts.Add(top1);
        verts.Add(bot1);

        norms.Add(n0);
        norms.Add(n0);
        norms.Add(n1);
        norms.Add(n1);

        if (!flip)
        {
            tris.Add(baseIdx);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 3);
        }
        else
        {
            tris.Add(baseIdx);
            tris.Add(baseIdx + 2);
            tris.Add(baseIdx + 1);
            tris.Add(baseIdx);
            tris.Add(baseIdx + 3);
            tris.Add(baseIdx + 2);
        }
    }

    private static void AddPerimeterWallQuad(
        List<Vector3> verts,
        List<Vector3> norms,
        List<int> tris,
        Vector3 b0,
        Vector3 b1,
        Vector3 t1,
        Vector3 t0)
    {
        Vector3 edgeDir = (b1 - b0).normalized;
        Vector3 outNormal = Vector3.Cross(Vector3.up, edgeDir).normalized;
        if (outNormal.sqrMagnitude < 0.001f)
        {
            outNormal = Vector3.forward;
        }
        AddFlatQuad(verts, norms, tris, b0, t0, t1, b1, outNormal);
    }

    /// <summary>
    /// Helper pembangun GameObject MeshFilter + MeshRenderer bebas Collider dan bebas ShadowCaster (OpenGLES3-safe).
    /// </summary>
    public static MeshRenderer CreateMeshPart(
        string name,
        Mesh mesh,
        Transform parent,
        Vector3 localPos,
        Vector3 localEuler,
        Vector3 localScale,
        Material sharedMat,
        List<Renderer> optionalRimCollector = null)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = localPos;
        obj.transform.localEulerAngles = localEuler;
        obj.transform.localScale = localScale;

        MeshFilter mf = obj.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = obj.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        if (sharedMat != null)
        {
            mr.sharedMaterial = sharedMat;
        }

        if (optionalRimCollector != null)
        {
            optionalRimCollector.Add(mr);
        }

        return mr;
    }
}
