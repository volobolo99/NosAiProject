namespace NosAi.Runtime.Tests;

public sealed class YoloV8DetectionDecoderTests
{
    [Fact]
    public void Decodes_channel_first_tensor_and_scales_normalized_center()
    {
        // [1, 6, 2]: cx,cy,w,h + two class scores for two candidates.
        float[] values =
        {
            0.25f, 0.75f, // cx
            0.50f, 0.25f, // cy
            0.10f, 0.20f, // width
            0.10f, 0.20f, // height
            0.90f, 0.10f, // class 0
            0.05f, 0.80f  // class 1
        };

        var decoder = new YoloV8DetectionDecoder(new[] { "mob", "npc" }, 0.35f);
        var result = decoder.Decode(
            new[] { new OnnxTensorOutput("output", new[] { 1, 6, 2 }, values) },
            1000,
            800);

        Assert.Equal(2, result.Count);
        Assert.Equal("mob", result[0].Kind);
        Assert.Equal(250, result[0].X, 6);
        Assert.Equal(400, result[0].Y, 6);
        Assert.Equal("npc", result[1].Kind);
        Assert.Equal(750, result[1].X, 6);
        Assert.Equal(200, result[1].Y, 6);
    }

    [Fact]
    public void Decodes_candidate_first_tensor()
    {
        float[] values =
        {
            100, 200, 40, 60, 0.1f, 0.95f,
            300, 400, 20, 20, 0.8f, 0.2f
        };

        var decoder = new YoloV8DetectionDecoder(new[] { "mob", "npc" }, normalizedCoordinates: false);
        var result = decoder.Decode(
            new[] { new OnnxTensorOutput("output", new[] { 1, 2, 6 }, values) },
            800,
            600);

        Assert.Equal(2, result.Count);
        Assert.Equal("npc", result[0].Kind);
        Assert.Equal(100, result[0].X, 6);
        Assert.Equal(200, result[0].Y, 6);
        Assert.Equal("mob", result[1].Kind);
    }

    [Fact]
    public void Rejects_low_confidence_and_malformed_tensor_without_fabricating_detections()
    {
        var decoder = new YoloV8DetectionDecoder(new[] { "mob" }, 0.5f);
        var result = decoder.Decode(
            new[]
            {
                new OnnxTensorOutput("low", new[] { 1, 5, 1 }, new[] { 0.5f, 0.5f, 0.2f, 0.2f, 0.49f }),
                new OnnxTensorOutput("bad", new[] { 1, 4, 1 }, new float[4])
            },
            640,
            640);

        Assert.Empty(result);
    }

    [Fact]
    public void Preserves_fail_closed_health_semantics()
    {
        var decoder = new YoloV8DetectionDecoder(new[] { "mob" });
        var result = decoder.Decode(
            new[] { new OnnxTensorOutput("output", new[] { 1, 5, 1 }, new[] { 0.5f, 0.5f, 0.2f, 0.2f, 0.9f }) },
            640,
            640);

        Assert.Single(result);
        Assert.True(double.IsNaN(result[0].HpRatio));
    }
}
