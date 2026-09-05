using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace NosAi.Runtime.Perception;

/// <summary>Model-independent ONNX detector configuration.</summary>
public sealed record OnnxDetectorOptions(
    string ModelPath,
    string InputName,
    int InputWidth,
    int InputHeight,
    float PixelScale = 1.0f / 255.0f)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ModelPath))
            throw new ArgumentException("ModelPath is required.", nameof(ModelPath));
        if (string.IsNullOrWhiteSpace(InputName))
            throw new ArgumentException("InputName is required.", nameof(InputName));
        if (InputWidth <= 0)
            throw new ArgumentOutOfRangeException(nameof(InputWidth));
        if (InputHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(InputHeight));
        if (!float.IsFinite(PixelScale) || PixelScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(PixelScale));
    }
}

/// <summary>Raw ONNX tensor copied out before session results are disposed.</summary>
public sealed record OnnxTensorOutput(
    string Name,
    int[] Dimensions,
    float[] Values);

/// <summary>
/// Model-specific post-processing boundary.
/// YOLO, RT-DETR or any other exported detector can provide a decoder without
/// changing capture, tracking or the canonical perception pipeline.
/// </summary>
public interface IOnnxDetectionDecoder
{
    IReadOnlyList<Detection> Decode(
        IReadOnlyList<OnnxTensorOutput> outputs,
        int sourceWidth,
        int sourceHeight);
}

/// <summary>
/// ONNX Runtime implementation of IObjectDetector.
/// It owns only inference/preprocessing; output semantics stay behind
/// IOnnxDetectionDecoder so the runtime is not coupled to one model family.
/// </summary>
public sealed class OnnxObjectDetector : IObjectDetector, IDisposable
{
    private readonly InferenceSession _session;
    private readonly OnnxDetectorOptions _options;
    private readonly IOnnxDetectionDecoder _decoder;
    private bool _disposed;

    public string Name { get; }

    private OnnxObjectDetector(
        InferenceSession session,
        OnnxDetectorOptions options,
        IOnnxDetectionDecoder decoder,
        string name)
    {
        _session = session;
        _options = options;
        _decoder = decoder;
        Name = name;
    }

    /// <summary>
    /// Creates the detector or fails closed with a named reason.
    /// Invalid/missing models never silently fall back to fabricated detections.
    /// </summary>
    public static bool TryCreate(
        OnnxDetectorOptions options,
        IOnnxDetectionDecoder decoder,
        out OnnxObjectDetector? detector,
        out string? unavailableReason,
        SessionOptions? sessionOptions = null,
        string name = "onnx-detector")
    {
        detector = null;
        unavailableReason = null;

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(decoder);

        try
        {
            options.Validate();

            if (!File.Exists(options.ModelPath))
            {
                unavailableReason = "onnx_model_not_found";
                return false;
            }

            var session = sessionOptions is null
                ? new InferenceSession(options.ModelPath)
                : new InferenceSession(options.ModelPath, sessionOptions);

            if (!session.InputMetadata.ContainsKey(options.InputName))
            {
                session.Dispose();
                unavailableReason = "onnx_input_name_not_found";
                return false;
            }

            detector = new OnnxObjectDetector(
                session,
                options,
                decoder,
                string.IsNullOrWhiteSpace(name) ? "onnx-detector" : name);
            return true;
        }
        catch (OnnxRuntimeException)
        {
            unavailableReason = "onnx_runtime_initialization_failed";
            return false;
        }
        catch (InvalidDataException)
        {
            unavailableReason = "onnx_model_invalid";
            return false;
        }
        catch (IOException)
        {
            unavailableReason = "onnx_model_io_failed";
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            unavailableReason = "onnx_model_access_denied";
            return false;
        }
    }

    public IReadOnlyList<Detection> Detect(CaptureFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(frame);

        if (!frame.HasPixels)
            return Array.Empty<Detection>();

        var tensor = CreateInputTensor(
            frame,
            _options.InputWidth,
            _options.InputHeight,
            _options.PixelScale);

        using var input = NamedOnnxValue.CreateFromTensor(_options.InputName, tensor);
        using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results =
            _session.Run(new[] { input });

        var outputs = new List<OnnxTensorOutput>(results.Count);
        foreach (var result in results)
        {
            var outputTensor = result.AsTensor<float>();
            outputs.Add(new OnnxTensorOutput(
                result.Name,
                outputTensor.Dimensions.ToArray(),
                outputTensor.ToArray()));
        }

        return _decoder.Decode(outputs, frame.Width, frame.Height)
            ?? Array.Empty<Detection>();
    }

    /// <summary>
    /// Converts BGRA pixels to a normalized RGB NCHW float tensor.
    /// Nearest-neighbour resize is intentionally deterministic and allocation
    /// bounded; higher-quality preprocessing can later be a separate backend.
    /// </summary>
    internal static DenseTensor<float> CreateInputTensor(
        CaptureFrame frame,
        int inputWidth,
        int inputHeight,
        float pixelScale)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (!frame.HasPixels) throw new ArgumentException("Frame has no pixels.", nameof(frame));
        if (inputWidth <= 0) throw new ArgumentOutOfRangeException(nameof(inputWidth));
        if (inputHeight <= 0) throw new ArgumentOutOfRangeException(nameof(inputHeight));
        if (!float.IsFinite(pixelScale) || pixelScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(pixelScale));

        var tensor = new DenseTensor<float>(new[] { 1, 3, inputHeight, inputWidth });
        var bgra = frame.Bgra.Span;

        for (var y = 0; y < inputHeight; y++)
        {
            var sourceY = Math.Min(frame.Height - 1, (int)((long)y * frame.Height / inputHeight));
            for (var x = 0; x < inputWidth; x++)
            {
                var sourceX = Math.Min(frame.Width - 1, (int)((long)x * frame.Width / inputWidth));
                var pixel = (sourceY * frame.Width + sourceX) * 4;

                var b = bgra[pixel];
                var g = bgra[pixel + 1];
                var r = bgra[pixel + 2];

                tensor[0, 0, y, x] = r * pixelScale;
                tensor[0, 1, y, x] = g * pixelScale;
                tensor[0, 2, y, x] = b * pixelScale;
            }
        }

        return tensor;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _session.Dispose();
    }
}

/// <summary>Fail-closed decoder useful while a model-specific decoder is absent.</summary>
public sealed class EmptyOnnxDetectionDecoder : IOnnxDetectionDecoder
{
    public IReadOnlyList<Detection> Decode(
        IReadOnlyList<OnnxTensorOutput> outputs,
        int sourceWidth,
        int sourceHeight)
        => Array.Empty<Detection>();
}
