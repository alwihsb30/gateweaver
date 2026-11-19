using System.Collections.Generic;
using UnityEngine;

public readonly struct UnistrokeRecognitionResult
{
    public readonly bool matched;
    public readonly NodeType gateType;
    public readonly float confidence;
    public readonly string symbolLabel;

    public UnistrokeRecognitionResult(bool matched, NodeType gateType, float confidence, string symbolLabel)
    {
        this.matched = matched;
        this.gateType = gateType;
        this.confidence = confidence;
        this.symbolLabel = symbolLabel;
    }
}

/// <summary>
/// Implementasi ringan, zero-dependency, dan low-GC dari algoritma $1 Unistroke Recognizer
/// untuk mengenali 5 simbol gerbang logika di udara (Mode Air-Weaving v6.0):
/// - '^' -> AND
/// - 'O' -> OR
/// - 'Z' -> XOR
/// - 'V' -> NAND
/// - '|' -> NOT (dengan prapemeriksaan proporsi bounding-box vertikal presisi)
/// Memakai rotasi terbatas (+/-22 derajat) agar '^' tidak tertukar dengan 'V' dan '|' tidak tertukar garis datar.
/// </summary>
public sealed class UnistrokeGestureRecognizer
{
    private const int ResamplePointCount = 32;
    private const float SquareSize = 1.0f;
    private const float HalfDiagonal = 0.70710678f; // 0.5 * sqrt(2)
    private const float AngleRangeRad = 22.0f * Mathf.Deg2Rad;
    private const float AnglePrecisionRad = 2.0f * Mathf.Deg2Rad;
    private const float GoldenRatioPhi = 0.61803399f; // 0.5 * (-1 + sqrt(5))

    private sealed class GestureTemplate
    {
        public readonly NodeType gateType;
        public readonly string symbolLabel;
        public readonly Vector2[] points;

        public GestureTemplate(NodeType gateType, string symbolLabel, Vector2[] normalizedPoints)
        {
            this.gateType = gateType;
            this.symbolLabel = symbolLabel;
            this.points = normalizedPoints;
        }
    }

    private readonly List<GestureTemplate> templates = new List<GestureTemplate>(16);

    // Buffer kerja pra-alokasi agar Recognize() bebas alokasi GC di runtime
    private readonly List<Vector2> workingSource = new List<Vector2>(128);
    private readonly Vector2[] resampledBuffer = new Vector2[ResamplePointCount];
    private readonly Vector2[] candidateBuffer = new Vector2[ResamplePointCount];
    private readonly Vector2[] rotatedBuffer = new Vector2[ResamplePointCount];

    public UnistrokeGestureRecognizer()
    {
        BuildTemplates();
    }

    /// <summary>
    /// Mengenali goresan 2D (sumbu +X kanan, +Y atas) menjadi salah satu dari 5 gerbang logika.
    /// Zero-GC setelah konstruktor.
    /// </summary>
    public UnistrokeRecognitionResult Recognize(
        IReadOnlyList<Vector2> rawPoints,
        float minSpan = 0.22f,
        float minConfidence = 0.72f)
    {
        if (rawPoints == null || rawPoints.Count < 8)
        {
            return new UnistrokeRecognitionResult(false, NodeType.AND, 0f, string.Empty);
        }

        ComputeBounds(rawPoints, out float minX, out float maxX, out float minY, out float maxY);
        float width = maxX - minX;
        float height = maxY - minY;
        float maxSpan = Mathf.Max(width, height);
        if (maxSpan < minSpan)
        {
            return new UnistrokeRecognitionResult(false, NodeType.AND, 0f, string.Empty);
        }

        float totalPathLength = ComputePathLength(rawPoints);
        if (totalPathLength < minSpan)
        {
            return new UnistrokeRecognitionResult(false, NodeType.AND, 0f, string.Empty);
        }

        Vector2 first = rawPoints[0];
        Vector2 last = rawPoints[rawPoints.Count - 1];
        float netDx = Mathf.Abs(last.x - first.x);
        float netDy = Mathf.Abs(last.y - first.y);

        // 1. Prapemeriksaan cepat garis vertikal '|' -> NOT (hindari distorsi ScaleToSquare pada garis tipis)
        bool isDominantVertical = height >= minSpan
                                  && height >= (width * 2.35f)
                                  && netDy >= (height * 0.70f)
                                  && totalPathLength <= (height * 1.48f);
        if (isDominantVertical)
        {
            float verticality = 1f - Mathf.Clamp01(width / Mathf.Max(0.0001f, height));
            float straightness = Mathf.Clamp01(netDy / Mathf.Max(0.0001f, totalPathLength));
            float notConfidence = Mathf.Clamp01(0.76f + (0.14f * verticality) + (0.10f * straightness));
            if (notConfidence >= minConfidence)
            {
                return new UnistrokeRecognitionResult(true, NodeType.NOT, notConfidence, "|");
            }
        }

        // 2. Tolak garis datar lurus murni (sapuan tangan biasa) agar tidak memicu spawn palsu
        bool isFlatHorizontalSweep = width >= (height * 2.75f)
                                     && netDx >= (width * 0.74f)
                                     && totalPathLength <= (width * 1.40f);
        if (isFlatHorizontalSweep)
        {
            return new UnistrokeRecognitionResult(false, NodeType.AND, 0f, string.Empty);
        }

        // 3. Jalankan pipeline $1 Unistroke (resample N=32, scale aman proporsional, translate ke origin)
        Resample(rawPoints, resampledBuffer);
        ScaleAndTranslateToOrigin(resampledBuffer, candidateBuffer);

        float bestDistance = float.MaxValue;
        GestureTemplate bestTemplate = null;

        for (int i = 0; i < templates.Count; i++)
        {
            GestureTemplate tpl = templates[i];
            float dist = DistanceAtBestAngle(candidateBuffer, tpl.points, -AngleRangeRad, AngleRangeRad, AnglePrecisionRad);
            if (dist < bestDistance)
            {
                bestDistance = dist;
                bestTemplate = tpl;
            }
        }

        if (bestTemplate == null)
        {
            return new UnistrokeRecognitionResult(false, NodeType.AND, 0f, string.Empty);
        }

        float confidence = Mathf.Clamp01(1f - (bestDistance / HalfDiagonal));

        // Penjaga tambahan untuk 'O' (OR): ujung awal & akhir lingkaran harus relatif menutup
        if (bestTemplate.gateType == NodeType.OR)
        {
            float endGap = Vector2.Distance(first, last);
            if (endGap > maxSpan * 0.62f)
            {
                confidence *= 0.80f;
            }
        }

        if (confidence < minConfidence)
        {
            return new UnistrokeRecognitionResult(false, bestTemplate.gateType, confidence, bestTemplate.symbolLabel);
        }

        return new UnistrokeRecognitionResult(true, bestTemplate.gateType, confidence, bestTemplate.symbolLabel);
    }

    /// <summary>
    /// Menghitung titik pusat kotak pembatas (bounding-box center) dari goresan 2D
    /// agar penempatan gerbang terasa tepat di tengah pola yang digambar pengguna.
    /// </summary>
    public static Vector2 ComputeStrokeBoundsCenter(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count == 0)
        {
            return Vector2.zero;
        }

        ComputeBounds(points, out float minX, out float maxX, out float minY, out float maxY);
        return new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);
    }

    public static float ComputeStrokeMaxSpan(IReadOnlyList<Vector2> points)
    {
        if (points == null || points.Count < 2)
        {
            return 0f;
        }

        ComputeBounds(points, out float minX, out float maxX, out float minY, out float maxY);
        return Mathf.Max(maxX - minX, maxY - minY);
    }

    private void BuildTemplates()
    {
        // AND: '^' (kiri-ke-kanan & kanan-ke-kiri)
        AddTemplate(NodeType.AND, "^", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(0.5f, 1f),
            new Vector2(1f, 0f)
        });
        AddTemplate(NodeType.AND, "^", new[]
        {
            new Vector2(1f, 0f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, 0f)
        });

        // NAND: 'V' (kiri-ke-kanan & kanan-ke-kiri)
        AddTemplate(NodeType.NAND, "V", new[]
        {
            new Vector2(0f, 1f),
            new Vector2(0.5f, 0f),
            new Vector2(1f, 1f)
        });
        AddTemplate(NodeType.NAND, "V", new[]
        {
            new Vector2(1f, 1f),
            new Vector2(0.5f, 0f),
            new Vector2(0f, 1f)
        });

        // OR: 'O' (lingkaran penuh CCW & CW dari 4 titik awal utama: atas, kanan, bawah, kiri)
        AddTemplate(NodeType.OR, "O", BuildCirclePolyline(startAngleDeg: 90f, clockwise: false));
        AddTemplate(NodeType.OR, "O", BuildCirclePolyline(startAngleDeg: 90f, clockwise: true));
        AddTemplate(NodeType.OR, "O", BuildCirclePolyline(startAngleDeg: 0f, clockwise: false));
        AddTemplate(NodeType.OR, "O", BuildCirclePolyline(startAngleDeg: 0f, clockwise: true));
        AddTemplate(NodeType.OR, "O", BuildCirclePolyline(startAngleDeg: -90f, clockwise: false));
        AddTemplate(NodeType.OR, "O", BuildCirclePolyline(startAngleDeg: -90f, clockwise: true));
        AddTemplate(NodeType.OR, "O", BuildCirclePolyline(startAngleDeg: 180f, clockwise: false));
        AddTemplate(NodeType.OR, "O", BuildCirclePolyline(startAngleDeg: 180f, clockwise: true));

        // XOR: 'Z' (maju, mundur, & cermin)
        AddTemplate(NodeType.XOR, "Z", new[]
        {
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0f, 0f),
            new Vector2(1f, 0f)
        });
        AddTemplate(NodeType.XOR, "Z", new[]
        {
            new Vector2(1f, 0f),
            new Vector2(0f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        });
        AddTemplate(NodeType.XOR, "Z", new[]
        {
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0f)
        });
        AddTemplate(NodeType.XOR, "Z", new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 1f),
            new Vector2(1f, 1f)
        });
    }

    private void AddTemplate(NodeType gateType, string symbolLabel, Vector2[] rawPolyline)
    {
        Vector2[] resampled = new Vector2[ResamplePointCount];
        Vector2[] normalized = new Vector2[ResamplePointCount];
        Resample(rawPolyline, resampled);
        ScaleAndTranslateToOrigin(resampled, normalized);
        templates.Add(new GestureTemplate(gateType, symbolLabel, normalized));
    }

    private static Vector2[] BuildCirclePolyline(float startAngleDeg, bool clockwise)
    {
        const int steps = 32;
        Vector2[] pts = new Vector2[steps + 1];
        float dir = clockwise ? -1f : 1f;
        for (int i = 0; i <= steps; i++)
        {
            float angleRad = (startAngleDeg + (dir * 360f * i / steps)) * Mathf.Deg2Rad;
            pts[i] = new Vector2(0.5f + (0.5f * Mathf.Cos(angleRad)), 0.5f + (0.5f * Mathf.Sin(angleRad)));
        }
        return pts;
    }

    private void Resample(IReadOnlyList<Vector2> source, Vector2[] destination)
    {
        workingSource.Clear();
        for (int i = 0; i < source.Count; i++)
        {
            workingSource.Add(source[i]);
        }

        float totalLength = ComputePathLength(workingSource);
        float interval = totalLength / (ResamplePointCount - 1);
        if (interval <= 0.00001f)
        {
            Vector2 p0 = workingSource[0];
            for (int i = 0; i < ResamplePointCount; i++)
            {
                destination[i] = p0;
            }
            return;
        }

        destination[0] = workingSource[0];
        int dstIdx = 1;
        float accumulated = 0f;
        int iSrc = 1;

        while (iSrc < workingSource.Count && dstIdx < ResamplePointCount)
        {
            Vector2 prev = workingSource[iSrc - 1];
            Vector2 curr = workingSource[iSrc];
            float segLen = Vector2.Distance(prev, curr);

            if (accumulated + segLen >= interval && segLen > 0.00001f)
            {
                float t = (interval - accumulated) / segLen;
                Vector2 q = new Vector2(
                    prev.x + (t * (curr.x - prev.x)),
                    prev.y + (t * (curr.y - prev.y)));
                destination[dstIdx] = q;
                dstIdx++;
                workingSource.Insert(iSrc, q);
                accumulated = 0f;
                iSrc++;
            }
            else
            {
                accumulated += segLen;
                iSrc++;
            }
        }

        Vector2 lastPt = workingSource[workingSource.Count - 1];
        while (dstIdx < ResamplePointCount)
        {
            destination[dstIdx] = lastPt;
            dstIdx++;
        }
    }

    private static void ScaleAndTranslateToOrigin(Vector2[] source, Vector2[] destination)
    {
        ComputeBounds(source, out float minX, out float maxX, out float minY, out float maxY);
        float w = maxX - minX;
        float h = maxY - minY;
        float safeW = Mathf.Max(w, Mathf.Max(0.0001f, h * 0.35f));
        float safeH = Mathf.Max(h, Mathf.Max(0.0001f, w * 0.35f));

        float sumX = 0f;
        float sumY = 0f;
        for (int i = 0; i < ResamplePointCount; i++)
        {
            float sx = ((source[i].x - minX) / safeW) * SquareSize;
            float sy = ((source[i].y - minY) / safeH) * SquareSize;
            destination[i] = new Vector2(sx, sy);
            sumX += sx;
            sumY += sy;
        }

        float cx = sumX / ResamplePointCount;
        float cy = sumY / ResamplePointCount;
        for (int i = 0; i < ResamplePointCount; i++)
        {
            destination[i] = new Vector2(destination[i].x - cx, destination[i].y - cy);
        }
    }

    private float DistanceAtBestAngle(Vector2[] points, Vector2[] templatePoints, float fromAngle, float toAngle, float threshold)
    {
        float x1 = (GoldenRatioPhi * fromAngle) + ((1f - GoldenRatioPhi) * toAngle);
        float f1 = DistanceAtAngle(points, templatePoints, x1);
        float x2 = ((1f - GoldenRatioPhi) * fromAngle) + (GoldenRatioPhi * toAngle);
        float f2 = DistanceAtAngle(points, templatePoints, x2);

        while (Mathf.Abs(toAngle - fromAngle) > threshold)
        {
            if (f1 < f2)
            {
                toAngle = x2;
                x2 = x1;
                f2 = f1;
                x1 = (GoldenRatioPhi * fromAngle) + ((1f - GoldenRatioPhi) * toAngle);
                f1 = DistanceAtAngle(points, templatePoints, x1);
            }
            else
            {
                fromAngle = x1;
                x1 = x2;
                f1 = f2;
                x2 = ((1f - GoldenRatioPhi) * fromAngle) + (GoldenRatioPhi * toAngle);
                f2 = DistanceAtAngle(points, templatePoints, x2);
            }
        }

        return Mathf.Min(f1, f2);
    }

    private float DistanceAtAngle(Vector2[] points, Vector2[] templatePoints, float radians)
    {
        float cos = Mathf.Cos(radians);
        float sin = Mathf.Sin(radians);
        float sumDist = 0f;

        for (int i = 0; i < ResamplePointCount; i++)
        {
            Vector2 p = points[i];
            rotatedBuffer[i] = new Vector2((p.x * cos) - (p.y * sin), (p.x * sin) + (p.y * cos));
            sumDist += Vector2.Distance(rotatedBuffer[i], templatePoints[i]);
        }

        return sumDist / ResamplePointCount;
    }

    private static void ComputeBounds(IReadOnlyList<Vector2> points, out float minX, out float maxX, out float minY, out float maxY)
    {
        minX = float.MaxValue;
        maxX = float.MinValue;
        minY = float.MaxValue;
        maxY = float.MinValue;

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = points[i];
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }
    }

    private static float ComputePathLength(IReadOnlyList<Vector2> points)
    {
        float len = 0f;
        for (int i = 1; i < points.Count; i++)
        {
            len += Vector2.Distance(points[i - 1], points[i]);
        }
        return len;
    }
}
