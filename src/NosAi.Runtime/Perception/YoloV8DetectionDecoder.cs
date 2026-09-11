using System.Globalization;

namespace NosAi.Runtime.Perception;

/// <summary>
/// Decoder for the common YOLOv8 detection export with output shaped either
/// [1, channels, candidates] or [1, candidates, channels]. The first four
/// values are cx, cy, width and height; remaining values are class scores.
/// Coordinates may be normalized or expressed in source pixels.
/// </summary>
public sealed class YoloV8DetectionDecoder : IOnnxDetectionDecoder
{
    private readonly IReadOnlyList<string> _classNames;
    private readonly float _confidenceThreshold;
    private readonly int _maxDetections;
    private readonly bool _normalizedCoordinates;

    public YoloV8DetectionDecoder(
        IReadOnlyList<string> classNames,
        float confidenceThreshold = 0.35f,
        int maxDetections = 128,
        bool normalizedCoordinates = true)
    {
        ArgumentNullException.ThrowIfNull(classNames);
        if (classNames.Count == 0) throw new ArgumentException("At least one class is required.", nameof(classNames));
        if (!float.IsFinite(confidenceThreshold) || confidenceThreshold <= 0 || confidenceThreshold > 1)
            throw new ArgumentOutOfRangeException(nameof(confidenceThreshold));
        if (maxDetections <= 0) throw new ArgumentOutOfRangeException(nameof(maxDetections));

        _classNames = classNames;
        _confidenceThreshold = confidenceThreshold;
        _maxDetections = maxDetections;
        _normalizedCoordinates = normalizedCoordinates;
    }

    public IReadOnlyList<Detection> Decode(
        IReadOnlyList<OnnxTensorOutput> outputs,
        int sourceWidth,
        int sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        if (sourceWidth <= 0 || sourceHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(sourceWidth));

        var detections = new List<ScoredDetection>();
        foreach (OnnxTensorOutput output in outputs)
        {
            if (!TryReadLayout(output.Dimensions, out int candidates, out int channels, out bool channelFirst))
                continue;
            if (channels < 5 || candidates <= 0)
                continue;

            int classCount = Math.Min(_classNames.Count, channels - 4);
            for (int candidate = 0; candidate < candidates; candidate++)
            {
                float cx = Read(output.Values, candidate, 0, candidates, channels, channelFirst);
                float cy = Read(output.Values, candidate, 1, candidates, channels, channelFirst);
                float width = Read(output.Values, candidate, 2, candidates, channels, channelFirst);
                float height = Read(output.Values, candidate, 3, candidates, channels, channelFirst);
                if (!FiniteBox(cx, cy, width, height)) continue;

                int bestClass = -1;
                float bestScore = 0;
                for (int classIndex = 0; classIndex < classCount; classIndex++)
                {
                    float score = Read(output.Values, candidate, classIndex + 4, candidates, channels, channelFirst);
                    if (float.IsFinite(score) && score > bestScore)
                    {
                        bestScore = score;
                        bestClass = classIndex;
                    }
                }

                if (bestClass < 0 || bestScore < _confidenceThreshold)
                    continue;

                double x = cx;
                double y = cy;
                double w = width;
                double h = height;
                if (_normalizedCoordinates)
                {
                    x *= sourceWidth;
                    y *= sourceHeight;
                    w *= sourceWidth;
                    h *= sourceHeight;
                }

                // Detection currently stores the observation point rather than a
                // rectangle. Keep the centre so tracking remains resolution-aware.
                double px = Math.Clamp(x, 0, sourceWidth - 1);
                double py = Math.Clamp(y, 0, sourceHeight - 1);
                detections.Add(new ScoredDetection(
                    new Detection(_classNames[bestClass], px, py, double.NaN),
                    bestScore,
                    w * h));
            }
        }

        return detections
            .OrderByDescending(d => d.Score)
            .ThenByDescending(d => d.Area)
            .Take(_maxDetections)
            .Select(d => d.Detection)
            .ToArray();
    }

    private static bool TryReadLayout(int[] dimensions, out int candidates, out int channels, out bool channelFirst)
    {
        candidates = channels = 0;
        channelFirst = false;
        if (dimensions is null || dimensions.Length != 3 || dimensions[0] != 1)
            return false;

        int a = dimensions[1];
        int b = dimensions[2];
        // YOLO exports commonly use [1,84,N]. A transposed export uses [1,N,84].
        // The smaller axis is treated as channels; reject pathological tensors.
        if (a <= 4 || b <= 4) return false;
        if (a <= b)
        {
            channels = a;
            candidates = b;
            channelFirst = true;
        }
        else
        {
            candidates = a;
            channels = b;
            channelFirst = false;
        }
        return channels >= 5 && candidates >= 1;
    }

    private static float Read(float[] values, int candidate, int channel, int candidates, int channels, bool channelFirst)
    {
        int index = channelFirst
            ? channel * candidates + candidate
            : candidate * channels + channel;
        return (uint)index < (uint)values.Length ? values[index] : float.NaN;
    }

    private static bool FiniteBox(float cx, float cy, float width, float height) =>
        float.IsFinite(cx) && float.IsFinite(cy) && float.IsFinite(width) && float.IsFinite(height)
        && width > 0 && height > 0;

    private readonly record struct ScoredDetection(Detection Detection, float Score, double Area);
}
