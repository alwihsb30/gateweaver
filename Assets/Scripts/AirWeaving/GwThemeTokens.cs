using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sumber kebenaran tunggal token desain GateWeaver AR v8.5
/// ("Smoked Optical Glass + Brushed Titanium") selaras 1:1 dengan Figma GW/Color &amp; GW/Metrics.
/// - Seluruh panel datar, sudut membulat 6px, border hairline 1px, nol neon glow, nol sudut potong 45 derajat.
/// - Ramah Android OpenGLES3 (Built-in Render Pipeline, tanpa real-time backdrop blur).
/// - Generator sprite prosedural 9-slice di-cache sekali (100% Zero-GC di runtime loop).
/// </summary>
public static class GwThemeTokens
{
    // =========================================================================
    // GW/Color - Palet Smoked Optical Glass + Brushed Titanium (1:1 Figma)
    // =========================================================================

    /// <summary>#12141C alpha 0.74f - Latar kaca gelap standar strip HUD &amp; pil bilah atas.</summary>
    public static readonly Color SmokedGlass = new Color(0x12 / 255f, 0x14 / 255f, 0x1C / 255f, 0.74f);

    /// <summary>#12141C alpha 0.66f - Latar kaca gelap tipis untuk trek gelombang / inset sekunder.</summary>
    public static readonly Color SmokedGlassSoft = new Color(0x12 / 255f, 0x14 / 255f, 0x1C / 255f, 0.66f);

    /// <summary>#12141C alpha 0.82f - Latar kaca gelap pekat untuk pil lipat, status pill, &amp; toast bawah.</summary>
    public static readonly Color SmokedGlassDense = new Color(0x12 / 255f, 0x14 / 255f, 0x1C / 255f, 0.82f);

    /// <summary>#12141C alpha 0.94f - Latar kaca gelap sangat pekat khusus overlay/modal pemblokir agar elemen di belakang tidak tembus.</summary>
    public static readonly Color SmokedGlassOverlay = new Color(0x12 / 255f, 0x14 / 255f, 0x1C / 255f, 0.94f);

    /// <summary>#12141C alpha 0.96f - Latar kaca gelap solid terpoles untuk panel utama &amp; header strip Layar Judul.</summary>
    public static readonly Color TitleSurfaceSolid = new Color(0x12 / 255f, 0x14 / 255f, 0x1C / 255f, 0.96f);

    /// <summary>#12141C alpha 0.98f - Latar kaca gelap ultra-pekat khusus panel Menu Drawer agar ikon Top HUD &amp; label 3D 100% tidak membayang.</summary>
    public static readonly Color DrawerSurfaceUltra = new Color(0x12 / 255f, 0x14 / 255f, 0x1C / 255f, 0.98f);

    /// <summary>#090B10 alpha 0.62f - Scrim peredup layar penuh di belakang modal/drawer pemblokir.</summary>
    public static readonly Color BackdropScrim = new Color(0x09 / 255f, 0x0B / 255f, 0x10 / 255f, 0.62f);

    /// <summary>#0A0C12 alpha 0.78f - Scrim gelap lembut khusus Layar Judul agar feed kamera AR di sisi kanan tetap terlihat samar seperti di Figma.</summary>
    public static readonly Color TitleBackdropScrim = new Color(0x0A / 255f, 0x0C / 255f, 0x12 / 255f, 0.78f);

    /// <summary>#0B0D14 alpha 0.94f - Backdrop penuh pekat khusus Peta Progresi Sirkuit (05_Level_Selector).</summary>
    public static readonly Color LevelMapBackdropSolid = new Color(0x0B / 255f, 0x0D / 255f, 0x14 / 255f, 0.94f);

    /// <summary>#1E2230 alpha 0.78f - Kartu/sel idle &amp; tombol kontrol titanium datar.</summary>
    public static readonly Color CardIdle = new Color(0x1E / 255f, 0x22 / 255f, 0x30 / 255f, 0.78f);

    /// <summary>#FFFFFF - Isi tombol primer kontras tinggi.</summary>
    public static readonly Color PrimaryFill = new Color(1.00f, 1.00f, 1.00f, 1.00f);

    /// <summary>#12141C - Teks gelap di atas PrimaryFill.</summary>
    public static readonly Color PrimaryText = new Color(0x12 / 255f, 0x14 / 255f, 0x1C / 255f, 1.00f);

    /// <summary>#FFFFFF alpha 0.15f - Border hairline 1px keadaan diam &amp; pembatas vertikal.</summary>
    public static readonly Color HairlineIdle = new Color(1.00f, 1.00f, 1.00f, 0.15f);

    /// <summary>#FFFFFF alpha 0.35f - Border hairline 1px keadaan aktif / fokus.</summary>
    public static readonly Color HairlineActive = new Color(1.00f, 1.00f, 1.00f, 0.35f);

    /// <summary>#F8FAFC - Teks primer kontras tinggi.</summary>
    public static readonly Color TextPrimary = new Color(0xF8 / 255f, 0xFA / 255f, 0xFC / 255f, 1.00f);

    /// <summary>#CBD5E1 - Teks sekunder / label konteks.</summary>
    public static readonly Color TextSecondary = new Color(0xCB / 255f, 0xD5 / 255f, 0xE1 / 255f, 1.00f);

    /// <summary>#38BDF8 - Sinyal 1 / HIGH / simbol IEEE aktif.</summary>
    public static readonly Color SignalHighTeal = new Color(0x38 / 255f, 0xBD / 255f, 0xF8 / 255f, 1.00f);

    /// <summary>#94A3B8 - Sinyal 0 / LOW / baseline.</summary>
    public static readonly Color SignalLowSlate = new Color(0x94 / 255f, 0xA3 / 255f, 0xB8 / 255f, 1.00f);

    /// <summary>#EF4444 - Border error / titik status terkunci / salah.</summary>
    public static readonly Color StatusErrorRed = new Color(0xEF / 255f, 0x44 / 255f, 0x44 / 255f, 1.00f);

    /// <summary>#FCA5A5 - Teks error / sel gagal / CEK SEL MERAH.</summary>
    public static readonly Color StatusErrorText = new Color(0xFC / 255f, 0xA5 / 255f, 0xA5 / 255f, 1.00f);

    /// <summary>#10B981 - Border lulus / cocok.</summary>
    public static readonly Color StatusPassEmerald = new Color(0x10 / 255f, 0xB9 / 255f, 0x81 / 255f, 1.00f);

    /// <summary>#34D399 - Teks lulus / OK.</summary>
    public static readonly Color StatusPassText = new Color(0x34 / 255f, 0xD3 / 255f, 0x99 / 255f, 1.00f);

    /// <summary>#F97316 - Aksen sakelar / peringatan parsial.</summary>
    public static readonly Color AccentSwitchOrange = new Color(0xF9 / 255f, 0x73 / 255f, 0x16 / 255f, 1.00f);

    /// <summary>#FFFFFF alpha 0.08f - Rel referensi halus untuk grafik gelombang kotak.</summary>
    public static readonly Color WaveRailFaint = new Color(1.00f, 1.00f, 1.00f, 0.08f);

    // =========================================================================
    // GW/Metrics - Geometri & Spasi UI (ruang referensi 1920x1080)
    // =========================================================================

    public const float CornerRadiusPx = 6f;
    public const float HairlineBorderPx = 1f;
    public const float TopInputStripHeight = 44f;
    public const float BottomOutputStripHeight = 56f;
    public const float CollapseButtonSize = 28f;
    public const float CollapsedPillHeight = 34f;
    public const float TopCollapsedPillWidth = 118f;
    public const float BottomCollapsedPillWidth = 124f;
    public const float StripSideMarginX = 24f;
    public const float TopStripTopOffsetY = -80f;
    public const float BottomStripBottomMarginY = 12f;
    public const float ToastBottomStripElevatedY = 82f;
    public const float StatusDotSizePx = 8f;

    public static readonly ColorBlock FlatGlassButtonColors = new ColorBlock
    {
        normalColor = Color.white,
        highlightedColor = new Color(0.94f, 0.96f, 1.00f, 1f),
        pressedColor = new Color(0.80f, 0.84f, 0.90f, 1f),
        selectedColor = Color.white,
        disabledColor = new Color(0.50f, 0.54f, 0.60f, 0.50f),
        colorMultiplier = 1f,
        fadeDuration = 0.06f
    };

    // =========================================================================
    // GENERATOR SPRITE PROSEDURAL 9-SLICE (RADIUS 6PX & HAIRLINE 1PX, ZERO-GC)
    // 2x supersampled (64x64 @ 200 PPU -> 1 UI px = 2 texels) agar tajam di 1080p/1440p.
    // =========================================================================

    private const int NineSliceTexSize = 64;
    private const float NineSlicePixelsPerUnit = 200f;
    private const float NineSliceOuterHalfTexels = 31.0f;
    private const float NineSliceRadiusTexels = CornerRadiusPx * 2.0f;      // 12 texels = 6 UI px
    private const float NineSliceBorderSliceTexels = 16.0f;                  // 16 texels = 8 UI px
    private const float NineSliceHairlineTexels = HairlineBorderPx * 2.0f;   // 2 texels  = 1 UI px
    private const int CircleTexSize = 32;

    private static Texture2D cachedRoundedRect6Tex;
    private static Sprite cachedRoundedRect6Sprite;
    private static Texture2D cachedHairlineBorder6Tex;
    private static Sprite cachedHairlineBorder6Sprite;
    private static Texture2D cachedCircleTex;
    private static Sprite cachedCircleSprite;
    private static Texture2D cachedCircleRingTex;
    private static Sprite cachedCircleRingSprite;

    public enum IeeeGateIconKind
    {
        And = 0,
        Or = 1,
        Not = 2,
        Nand = 3,
        Xor = 4
    }

    public enum FingerPoseIconKind
    {
        OneIndex = 0,
        TwoV = 1,
        ThumbUp = 2,
        ThreeFingers = 3,
        FourFingers = 4
    }

    public enum GestureGuideIconKind
    {
        FistBase = 0,
        FistOrangeDot = 1,
        PinchCyan = 2,
        TwoHandCrosshair = 3,
        TwoHandChevrons = 4,
        CameraTip = 5
    }

    private static readonly Texture2D[] cachedIeeeGateTexs = new Texture2D[5];
    private static readonly Sprite[] cachedIeeeGateSprites = new Sprite[5];
    private static readonly Texture2D[] cachedFingerPoseTexs = new Texture2D[5];
    private static readonly Sprite[] cachedFingerPoseSprites = new Sprite[5];
    private static readonly Texture2D[] cachedGestureGuideTexs = new Texture2D[6];
    private static readonly Sprite[] cachedGestureGuideSprites = new Sprite[6];

    public static Color GetSignalColor(bool isHigh)
    {
        return isHigh ? SignalHighTeal : SignalLowSlate;
    }

    public static Sprite GetRoundedRect6Sprite()
    {
        if (cachedRoundedRect6Sprite == null)
        {
            cachedRoundedRect6Sprite = CreateNineSliceRoundedSprite(
                "GW_V85_Rect6_Fill",
                isHairlineRing: false,
                out cachedRoundedRect6Tex);
        }

        return cachedRoundedRect6Sprite;
    }

    public static Sprite GetHairlineBorder6Sprite()
    {
        if (cachedHairlineBorder6Sprite == null)
        {
            cachedHairlineBorder6Sprite = CreateNineSliceRoundedSprite(
                "GW_V85_Rect6_Hairline1px",
                isHairlineRing: true,
                out cachedHairlineBorder6Tex);
        }

        return cachedHairlineBorder6Sprite;
    }

    public static Sprite GetCircleSprite()
    {
        if (cachedCircleSprite == null)
        {
            cachedCircleSprite = CreateCircleSprite("GW_V85_CircleDot", out cachedCircleTex);
        }

        return cachedCircleSprite;
    }

    public static void ApplyRoundedRect6(Image img)
    {
        if (img == null)
        {
            return;
        }

        Sprite sprite = GetRoundedRect6Sprite();
        if (img.sprite != sprite)
        {
            img.sprite = sprite;
        }
        if (img.type != Image.Type.Sliced)
        {
            img.type = Image.Type.Sliced;
        }
        if (Mathf.Abs(img.pixelsPerUnitMultiplier - 1f) > 0.001f)
        {
            img.pixelsPerUnitMultiplier = 1f;
        }
    }

    public static void ApplyRoundedRect6(Image img, Color color)
    {
        if (img == null)
        {
            return;
        }

        ApplyRoundedRect6(img);
        if (img.color != color)
        {
            img.color = color;
        }
    }

    public static void ApplyHairlineBorder6(Image img)
    {
        if (img == null)
        {
            return;
        }

        Sprite sprite = GetHairlineBorder6Sprite();
        if (img.sprite != sprite)
        {
            img.sprite = sprite;
        }
        if (img.type != Image.Type.Sliced)
        {
            img.type = Image.Type.Sliced;
        }
        if (Mathf.Abs(img.pixelsPerUnitMultiplier - 1f) > 0.001f)
        {
            img.pixelsPerUnitMultiplier = 1f;
        }
    }

    public static void ApplyHairlineBorder6(Image img, Color color)
    {
        if (img == null)
        {
            return;
        }

        ApplyHairlineBorder6(img);
        if (img.color != color)
        {
            img.color = color;
        }
    }

    public static Image AttachHairlineBorder(RectTransform parent, string name, Color borderColor)
    {
        if (parent == null)
        {
            return null;
        }

        Transform existing = parent.Find(name);
        GameObject borderObj = existing != null
            ? existing.gameObject
            : new GameObject(name, typeof(RectTransform), typeof(Image));
        if (existing == null)
        {
            borderObj.transform.SetParent(parent, false);
        }

        RectTransform rect = borderObj.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image img = borderObj.GetComponent<Image>();
        ApplyHairlineBorder6(img, borderColor);
        img.raycastTarget = false;
        return img;
    }

    private static Sprite CreateNineSliceRoundedSprite(string spriteName, bool isHairlineRing, out Texture2D tex)
    {
        const int size = NineSliceTexSize;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            name = spriteName + "_Tex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float innerHalf = Mathf.Max(1f, NineSliceOuterHalfTexels - NineSliceRadiusTexels);

        for (int y = 0; y < size; y++)
        {
            float py = Mathf.Abs((y + 0.5f) - half);
            float dy = py - innerHalf;
            float qy = Mathf.Max(dy, 0f);
            int rowOffset = y * size;

            for (int x = 0; x < size; x++)
            {
                float px = Mathf.Abs((x + 0.5f) - half);
                float dx = px - innerHalf;
                float qx = Mathf.Max(dx, 0f);

                // Exact 2D rounded-box SDF (signed distance in texels, 2 texels = 1 UI px)
                float outsideDist = Mathf.Sqrt((qx * qx) + (qy * qy));
                float insideDist = Mathf.Min(Mathf.Max(dx, dy), 0f);
                float dist = outsideDist + insideDist - NineSliceRadiusTexels;

                float alpha;
                if (!isHairlineRing)
                {
                    alpha = Mathf.Clamp01(0.5f - dist);
                }
                else
                {
                    float outerAlpha = Mathf.Clamp01(0.5f - dist);
                    float innerAlpha = Mathf.Clamp01(0.5f + (dist + NineSliceHairlineTexels));
                    alpha = outerAlpha * innerAlpha;
                }

                byte aByte = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[rowOffset + x] = new Color32(255, 255, 255, aByte);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Vector4 border = new Vector4(
            NineSliceBorderSliceTexels,
            NineSliceBorderSliceTexels,
            NineSliceBorderSliceTexels,
            NineSliceBorderSliceTexels);

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            NineSlicePixelsPerUnit,
            0,
            SpriteMeshType.FullRect,
            border);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite CreateCircleSprite(string spriteName, out Texture2D tex)
    {
        const int size = CircleTexSize;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            name = spriteName + "_Tex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float radius = half - 1.5f;

        for (int y = 0; y < size; y++)
        {
            float dy = (y + 0.5f) - half;
            int rowOffset = y * size;

            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) - half;
                float dist = Mathf.Sqrt((dx * dx) + (dy * dy)) - radius;
                float alpha = Mathf.Clamp01(0.5f - dist);
                byte aByte = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[rowOffset + x] = new Color32(255, 255, 255, aByte);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    public static Sprite GetCircleRingSprite()
    {
        if (cachedCircleRingSprite == null)
        {
            cachedCircleRingSprite = CreateCircleRingSprite("GW_V85_CircleRing", out cachedCircleRingTex);
        }

        return cachedCircleRingSprite;
    }

    public static Sprite GetIeeeGateSprite(IeeeGateIconKind kind)
    {
        int idx = Mathf.Clamp((int)kind, 0, cachedIeeeGateSprites.Length - 1);
        if (cachedIeeeGateSprites[idx] == null)
        {
            cachedIeeeGateSprites[idx] = CreateIeeeGateSprite((IeeeGateIconKind)idx, out cachedIeeeGateTexs[idx]);
        }

        return cachedIeeeGateSprites[idx];
    }

    public static Sprite GetFingerPoseSprite(FingerPoseIconKind kind)
    {
        int idx = Mathf.Clamp((int)kind, 0, cachedFingerPoseSprites.Length - 1);
        if (cachedFingerPoseSprites[idx] == null)
        {
            cachedFingerPoseSprites[idx] = CreateFingerPoseSprite((FingerPoseIconKind)idx, out cachedFingerPoseTexs[idx]);
        }

        return cachedFingerPoseSprites[idx];
    }

    public static Sprite GetGestureGuideSprite(GestureGuideIconKind kind)
    {
        int idx = Mathf.Clamp((int)kind, 0, cachedGestureGuideSprites.Length - 1);
        if (cachedGestureGuideSprites[idx] == null)
        {
            cachedGestureGuideSprites[idx] = CreateGestureGuideSprite((GestureGuideIconKind)idx, out cachedGestureGuideTexs[idx]);
        }

        return cachedGestureGuideSprites[idx];
    }

    private static Sprite CreateCircleRingSprite(string spriteName, out Texture2D tex)
    {
        const int size = CircleTexSize;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            name = spriteName + "_Tex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        float half = size * 0.5f;
        float radius = half - 2.5f;
        const float halfStroke = 1.35f;

        for (int y = 0; y < size; y++)
        {
            float dy = (y + 0.5f) - half;
            int rowOffset = y * size;

            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) - half;
                float ringDist = Mathf.Abs(Mathf.Sqrt((dx * dx) + (dy * dy)) - radius);
                float alpha = Mathf.Clamp01(0.5f - (ringDist - halfStroke));
                byte aByte = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[rowOffset + x] = new Color32(255, 255, 255, aByte);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite CreateIeeeGateSprite(IeeeGateIconKind kind, out Texture2D tex)
    {
        const int width = 96;
        const int height = 56;
        string spriteName = "GW_V85_IEEE_" + kind;
        tex = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
        {
            name = spriteName + "_Tex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[width * height];
        const float halfStroke = 1.25f;

        for (int y = 0; y < height; y++)
        {
            float py = y + 0.5f;
            int rowOffset = y * width;

            for (int x = 0; x < width; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, py);
                float d = EvaluateIeeeGateSdf(p, kind);
                float alpha = Mathf.Clamp01(0.5f - (d - halfStroke));
                byte aByte = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[rowOffset + x] = new Color32(255, 255, 255, aByte);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, width, height),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static float EvaluateIeeeGateSdf(Vector2 p, IeeeGateIconKind kind)
    {
        float d = 1000f;

        switch (kind)
        {
            case IeeeGateIconKind.And:
            {
                d = Mathf.Min(d, SdSegment(p, new Vector2(10f, 20f), new Vector2(28f, 20f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(10f, 36f), new Vector2(28f, 36f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(28f, 12f), new Vector2(28f, 44f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(28f, 44f), new Vector2(48f, 44f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(28f, 12f), new Vector2(48f, 12f)));
                if (p.x >= 47.5f)
                {
                    d = Mathf.Min(d, SdCircleRing(p, new Vector2(48f, 28f), 16f));
                }
                d = Mathf.Min(d, SdSegment(p, new Vector2(64f, 28f), new Vector2(84f, 28f)));
                break;
            }
            case IeeeGateIconKind.Nand:
            {
                d = Mathf.Min(d, SdSegment(p, new Vector2(8f, 20f), new Vector2(24f, 20f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(8f, 36f), new Vector2(24f, 36f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(24f, 12f), new Vector2(24f, 44f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(24f, 44f), new Vector2(43f, 44f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(24f, 12f), new Vector2(43f, 12f)));
                if (p.x >= 42.5f)
                {
                    d = Mathf.Min(d, SdCircleRing(p, new Vector2(43f, 28f), 16f));
                }
                d = Mathf.Min(d, SdCircleRing(p, new Vector2(63.8f, 28f), 4.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(68.2f, 28f), new Vector2(86f, 28f)));
                break;
            }
            case IeeeGateIconKind.Not:
            {
                d = Mathf.Min(d, SdSegment(p, new Vector2(10f, 28f), new Vector2(28f, 28f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(28f, 12f), new Vector2(28f, 44f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(28f, 44f), new Vector2(58f, 28f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(28f, 12f), new Vector2(58f, 28f)));
                d = Mathf.Min(d, SdCircleRing(p, new Vector2(62.8f, 28f), 4.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(67.2f, 28f), new Vector2(86f, 28f)));
                break;
            }
            case IeeeGateIconKind.Or:
            case IeeeGateIconKind.Xor:
            {
                bool isXor = kind == IeeeGateIconKind.Xor;
                float shiftX = isXor ? 3f : 0f;

                float leadStopX = isXor ? 25.5f : 29.5f;
                d = Mathf.Min(d, SdSegment(p, new Vector2(10f, 20f), new Vector2(leadStopX, 20f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(10f, 36f), new Vector2(leadStopX, 36f)));

                // Busur belakang cekung utama (dan busur kedua paralel untuk XOR)
                d = Mathf.Min(d, SdQuadBezier(p, new Vector2(25f + shiftX, 12f), new Vector2(35f + shiftX, 28f), new Vector2(25f + shiftX, 44f)));
                if (isXor)
                {
                    d = Mathf.Min(d, SdQuadBezier(p, new Vector2(21f, 12f), new Vector2(31f, 28f), new Vector2(21f, 44f)));
                }

                // Sisi atas dan bawah melengkung tajam menuju hidung kanan
                d = Mathf.Min(d, SdSegment(p, new Vector2(25f + shiftX, 44f), new Vector2(37f + shiftX, 44f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(25f + shiftX, 12f), new Vector2(37f + shiftX, 12f)));
                d = Mathf.Min(d, SdQuadBezier(p, new Vector2(37f + shiftX, 44f), new Vector2(55f + shiftX, 43f), new Vector2(65f + shiftX, 28f)));
                d = Mathf.Min(d, SdQuadBezier(p, new Vector2(37f + shiftX, 12f), new Vector2(55f + shiftX, 13f), new Vector2(65f + shiftX, 28f)));

                d = Mathf.Min(d, SdSegment(p, new Vector2(65f + shiftX, 28f), new Vector2(85f, 28f)));
                break;
            }
        }

        return d;
    }

    private static Sprite CreateFingerPoseSprite(FingerPoseIconKind kind, out Texture2D tex)
    {
        const int size = 64;
        string spriteName = "GW_V85_Pose_" + kind;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            name = spriteName + "_Tex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        const float halfStroke = 1.20f;

        for (int y = 0; y < size; y++)
        {
            float py = y + 0.5f;
            int rowOffset = y * size;

            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, py);
                float d = EvaluateFingerPoseSdf(p, kind);
                float alpha = Mathf.Clamp01(0.5f - (d - halfStroke));
                byte aByte = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[rowOffset + x] = new Color32(255, 255, 255, aByte);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static float EvaluateFingerPoseSdf(Vector2 p, FingerPoseIconKind kind)
    {
        float d = 1000f;

        switch (kind)
        {
            case FingerPoseIconKind.OneIndex:
            {
                d = Mathf.Min(d, SdRoundedBoxOutline(p, new Vector2(32f, 20f), new Vector2(10.5f, 9.5f), 3.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(26f, 29.5f), new Vector2(26f, 47f)));
                d = Mathf.Min(d, SdCircleDisc(p, new Vector2(26f, 49f), 1.4f));
                break;
            }
            case FingerPoseIconKind.TwoV:
            {
                d = Mathf.Min(d, SdRoundedBoxOutline(p, new Vector2(32f, 20f), new Vector2(10.5f, 9.5f), 3.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(28f, 29.5f), new Vector2(23f, 47f)));
                d = Mathf.Min(d, SdCircleDisc(p, new Vector2(22.5f, 49f), 1.4f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(36f, 29.5f), new Vector2(41f, 47f)));
                d = Mathf.Min(d, SdCircleDisc(p, new Vector2(41.5f, 49f), 1.4f));
                break;
            }
            case FingerPoseIconKind.ThumbUp:
            {
                d = Mathf.Min(d, SdRoundedBoxOutline(p, new Vector2(35f, 21f), new Vector2(10f, 9.5f), 3.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(25f, 25f), new Vector2(25f, 45f)));
                d = Mathf.Min(d, SdCircleDisc(p, new Vector2(25f, 47f), 1.5f));
                break;
            }
            case FingerPoseIconKind.ThreeFingers:
            {
                d = Mathf.Min(d, SdRoundedBoxOutline(p, new Vector2(32f, 20f), new Vector2(11.5f, 9.5f), 3.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(25.5f, 29.5f), new Vector2(25.5f, 47f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(32.0f, 29.5f), new Vector2(32.0f, 48.5f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(38.5f, 29.5f), new Vector2(38.5f, 47f)));
                break;
            }
            case FingerPoseIconKind.FourFingers:
            {
                d = Mathf.Min(d, SdRoundedBoxOutline(p, new Vector2(32f, 20f), new Vector2(13f, 9.5f), 3.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(23.0f, 29.5f), new Vector2(23.0f, 46.5f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(29.0f, 29.5f), new Vector2(29.0f, 48.5f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(35.0f, 29.5f), new Vector2(35.0f, 48.5f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(41.0f, 29.5f), new Vector2(41.0f, 46.5f)));
                break;
            }
        }

        return d;
    }

    private static Sprite CreateGestureGuideSprite(GestureGuideIconKind kind, out Texture2D tex)
    {
        const int size = 64;
        string spriteName = "GW_V85_GestureIcon_" + kind;
        tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            name = spriteName + "_Tex",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color32[] pixels = new Color32[size * size];
        const float halfStroke = 1.35f;

        for (int y = 0; y < size; y++)
        {
            float py = y + 0.5f;
            int rowOffset = y * size;

            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, py);
                float d = EvaluateGestureGuideSdf(p, kind);
                float alpha = Mathf.Clamp01(0.5f - (d - halfStroke));
                byte aByte = (byte)Mathf.RoundToInt(alpha * 255f);
                pixels[rowOffset + x] = new Color32(255, 255, 255, aByte);
            }
        }

        tex.SetPixels32(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        Sprite sprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f),
            100f,
            0,
            SpriteMeshType.FullRect);
        sprite.name = spriteName;
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static float EvaluateGestureGuideSdf(Vector2 p, GestureGuideIconKind kind)
    {
        float d = 1000f;

        switch (kind)
        {
            case GestureGuideIconKind.FistBase:
            {
                d = Mathf.Min(d, SdRoundedBoxOutline(p, new Vector2(32f, 26f), new Vector2(13.5f, 11.5f), 4.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(19.5f, 31f), new Vector2(44.5f, 31f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(27.5f, 31f), new Vector2(27.5f, 37.5f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(36.5f, 31f), new Vector2(36.5f, 37.5f)));
                break;
            }
            case GestureGuideIconKind.FistOrangeDot:
            {
                d = Mathf.Min(d, SdCircleDisc(p, new Vector2(32f, 45.5f), 3.4f));
                break;
            }
            case GestureGuideIconKind.PinchCyan:
            {
                d = Mathf.Min(d, SdCircleRing(p, new Vector2(32f, 34f), 6.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(19f, 15f), new Vector2(28f, 29f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(45f, 15f), new Vector2(36f, 29f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(32f, 43f), new Vector2(32f, 49f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(17f, 34f), new Vector2(23f, 34f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(41f, 34f), new Vector2(47f, 34f)));
                break;
            }
            case GestureGuideIconKind.TwoHandCrosshair:
            {
                d = Mathf.Min(d, SdCircleRing(p, new Vector2(32f, 32f), 5.2f));
                d = Mathf.Min(d, SdSegment(p, new Vector2(32f, 20f), new Vector2(32f, 44f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(22f, 32f), new Vector2(42f, 32f)));
                break;
            }
            case GestureGuideIconKind.TwoHandChevrons:
            {
                d = Mathf.Min(d, SdSegment(p, new Vector2(18f, 38.5f), new Vector2(11.5f, 32f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(11.5f, 32f), new Vector2(18f, 25.5f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(46f, 38.5f), new Vector2(52.5f, 32f)));
                d = Mathf.Min(d, SdSegment(p, new Vector2(52.5f, 32f), new Vector2(46f, 25.5f)));
                break;
            }
            case GestureGuideIconKind.CameraTip:
            {
                d = Mathf.Min(d, SdRoundedBoxOutline(p, new Vector2(32f, 32f), new Vector2(19f, 13.5f), 3.5f));
                d = Mathf.Min(d, SdCircleRing(p, new Vector2(32f, 32f), 6.5f));
                break;
            }
        }

        return d;
    }

    private static float SdSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a;
        Vector2 ba = b - a;
        float denom = (ba.x * ba.x) + (ba.y * ba.y);
        float h = denom > 0.0001f ? Mathf.Clamp01(((pa.x * ba.x) + (pa.y * ba.y)) / denom) : 0f;
        float dx = pa.x - (ba.x * h);
        float dy = pa.y - (ba.y * h);
        return Mathf.Sqrt((dx * dx) + (dy * dy));
    }

    private static float SdQuadBezier(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
    {
        const int steps = 8;
        float minD = 1000f;
        Vector2 prev = p0;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            float u = 1f - t;
            Vector2 cur = (u * u * p0) + (2f * u * t * p1) + (t * t * p2);
            float d = SdSegment(p, prev, cur);
            if (d < minD)
            {
                minD = d;
            }
            prev = cur;
        }
        return minD;
    }

    private static float SdCircleRing(Vector2 p, Vector2 center, float radius)
    {
        float dx = p.x - center.x;
        float dy = p.y - center.y;
        return Mathf.Abs(Mathf.Sqrt((dx * dx) + (dy * dy)) - radius);
    }

    private static float SdCircleDisc(Vector2 p, Vector2 center, float radius)
    {
        float dx = p.x - center.x;
        float dy = p.y - center.y;
        return Mathf.Sqrt((dx * dx) + (dy * dy)) - radius;
    }

    private static float SdRoundedBoxOutline(Vector2 p, Vector2 center, Vector2 halfSize, float cornerRadius)
    {
        float px = Mathf.Abs(p.x - center.x) - (halfSize.x - cornerRadius);
        float py = Mathf.Abs(p.y - center.y) - (halfSize.y - cornerRadius);
        float qx = Mathf.Max(px, 0f);
        float qy = Mathf.Max(py, 0f);
        float outside = Mathf.Sqrt((qx * qx) + (qy * qy));
        float inside = Mathf.Min(Mathf.Max(px, py), 0f);
        return Mathf.Abs(outside + inside - cornerRadius);
    }
}
