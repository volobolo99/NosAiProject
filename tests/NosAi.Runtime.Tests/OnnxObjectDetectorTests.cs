using NosAi.Runtime.Contracts;
using NosAi.Runtime.Perception;
using Xunit;

namespace NosAi.Runtime.Tests;

public sealed class OnnxObjectDetectorTests
{
    [Fact]
    public void Missing_model_fails_closed_with_named_reason()
    {
        var options = new OnnxDetectorOptions(
            ModelPath: Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".onnx"),
            InputName: "images",
            InputWidth: 640,
            InputHeight: 640);

        var opened = OnnxObjectDetector.TryCreate(
            options,
            new EmptyOnnxDetectionDecoder(),
            out var detector,
            out var reason);

        Assert.False(opened);
        Assert.Null(detector);
        Assert.Equal("onnx_model_not_found", reason);
    }

    [Fact]
    public void Preprocess_converts_bgra_to_rgb_nchw()
    {
        const int width = 2;
        const int height = 1;
        var pixels = new byte[]
        {
            10, 20, 30, 255,
            40, 50, 60, 255
        };
        var frame = new CaptureFrame(
            width,
            height,
            pixels,
            DataSourceKind.Simulated,
            DateTime.UtcNow);

        var tensor = OnnxObjectDetector.CreateInputTensor(
            frame,
            inputWidth: 2,
            inputHeight: 1,
            pixelScale: 1.0f);

        Assert.Equal(new[] { 1, 3, 1, 2 }, tensor.Dimensions.ToArray());

        Assert.Equal(30f, tensor[0, 0, 0, 0]);
        Assert.Equal(60f, tensor[0, 0, 0, 1]);
        Assert.Equal(20f, tensor[0, 1, 0, 0]);
        Assert.Equal(50f, tensor[0, 1, 0, 1]);
        Assert.Equal(10f, tensor[0, 2, 0, 0]);
        Assert.Equal(40f, tensor[0, 2, 0, 1]);
    }

    [Fact]
    public void Preprocess_resizes_deterministically()
    {
        const int width = 2;
        const int height = 2;
        var pixels = new byte[width * height * 4];

        WritePixel(pixels, width, 0, 0, r: 10, g: 0, b: 0);
        WritePixel(pixels, width, 1, 0, r: 20, g: 0, b: 0);
        WritePixel(pixels, width, 0, 1, r: 30, g: 0, b: 0);
        WritePixel(pixels, width, 1, 1, r: 40, g: 0, b: 0);

        var frame = new CaptureFrame(
            width,
            height,
            pixels,
            DataSourceKind.Simulated,
            DateTime.UtcNow);

        var tensor = OnnxObjectDetector.CreateInputTensor(
            frame,
            inputWidth: 1,
            inputHeight: 1,
            pixelScale: 1.0f);

        Assert.Equal(10f, tensor[0, 0, 0, 0]);
    }

    [Fact]
    public void Empty_decoder_never_fabricates_detections()
    {
        var decoder = new EmptyOnnxDetectionDecoder();
        var outputs = new[]
        {
            new OnnxTensorOutput(
                "output0",
                new[] { 1, 1, 6 },
                new float[] { 1, 2, 3, 4, 0.99f, 1 })
        };

        var detections = decoder.Decode(outputs, 1920, 1080);

        Assert.Empty(detections);
    }

    private static void WritePixel(byte[] bgra, int width, int x, int y, byte r, byte g, byte b)
    {
        var i = (y * width + x) * 4;
        bgra[i] = b;
        bgra[i + 1] = g;
        bgra[i + 2] = r;
        bgra[i + 3] = 255;
    }
}
