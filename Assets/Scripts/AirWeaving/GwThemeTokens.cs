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
}
