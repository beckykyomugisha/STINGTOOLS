using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace Planscape.Infrastructure.Services.PhotoPipeline;

/// <summary>
/// Phase 178b — ONNX-backed face detector wrapping YuNet
/// (face_detection_yunet_2023mar.onnx). YuNet is the recommended
/// CPU-only face detector in OpenCV's model zoo: ~225 KB, &lt; 30 ms per
/// 320×320 inference on a 4-core CPU, Apache-2.0 licence.
///
/// Model file location is read from the <c>PLANSCAPE_FACE_MODEL_PATH</c>
/// environment variable, or
/// <c>/var/lib/planscape/onnx/face_detection_yunet_2023mar.onnx</c> by
/// default. The worker container downloads the model at build time
/// from the OpenCV Zoo public CDN (no model bytes in this repo). On
/// missing model the detector logs a warning and behaves as a no-op
/// (so the redaction pipeline downgrades to "watermark only" rather
/// than failing the whole publish step).
///
/// The class is registered as a singleton in Program.cs when
/// <c>PLANSCAPE_ROLE = worker</c>; the InferenceSession is loaded
/// once and shared across all RedactPublishedPhotoJob invocations.
/// </summary>
public sealed class OnnxFaceDetector : IFaceDetector, IDisposable
{
    // YuNet 2023mar input is fixed-size; we resize the source bitmap
    // to fit. 320×320 is the official "small" size — favours speed
    // over recall. Bump to 640×640 to catch more faces if CPU permits.
    private const int InputW = 320;
    private const int InputH = 320;
    private const float ConfidenceThreshold = 0.6f;     // YuNet head score >= 0.6 = face
    private const float NmsIou = 0.3f;

    private readonly ILogger<OnnxFaceDetector> _logger;
    private readonly InferenceSession? _session;
    private readonly string? _inputName;

    public OnnxFaceDetector(ILogger<OnnxFaceDetector> logger)
    {
        _logger = logger;
        var modelPath = Environment.GetEnvironmentVariable("PLANSCAPE_FACE_MODEL_PATH")
                     ?? "/var/lib/planscape/onnx/face_detection_yunet_2023mar.onnx";
        if (!File.Exists(modelPath))
        {
            _logger.LogWarning(
                "OnnxFaceDetector: model not found at {Path} — face blur will be skipped. " +
                "Set PLANSCAPE_FACE_MODEL_PATH or place the ONNX file at the default location.",
                modelPath);
            return;
        }
        try
        {
            var opts = new SessionOptions
            {
                IntraOpNumThreads = Math.Max(2, Environment.ProcessorCount / 2),
                ExecutionMode     = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };
            _session = new InferenceSession(modelPath, opts);
            _inputName = _session.InputMetadata.Keys.FirstOrDefault();
            _logger.LogInformation("OnnxFaceDetector: loaded {Path} (input={Input})", modelPath, _inputName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OnnxFaceDetector: failed to load {Path}", modelPath);
            _session = null;
        }
    }

    public Task<IReadOnlyList<SKRectI>> DetectAsync(SKBitmap source, CancellationToken ct)
    {
        if (_session == null || _inputName == null)
            return Task.FromResult<IReadOnlyList<SKRectI>>(Array.Empty<SKRectI>());

        try
        {
            var (tensor, scaleX, scaleY) = BuildInputTensor(source);
            var input  = NamedOnnxValue.CreateFromTensor(_inputName, tensor);
            using var results = _session.Run(new[] { input });

            // YuNet outputs three (loc, iou, conf) heads at strides 8/16/32.
            // The simplest path — and the one OpenCV's FaceDetectorYN uses
            // internally — is to read the merged output if the model has
            // been exported with post-processing baked in. The 2023mar
            // variant ships post-processed outputs with shape [N, 15] where
            // each row = [x0, y0, w, h, lmk1x, lmk1y, …, conf].
            // We probe for the simplest shape first.
            var primary = results.FirstOrDefault();
            if (primary?.Value is not Tensor<float> tensorOut)
                return Task.FromResult<IReadOnlyList<SKRectI>>(Array.Empty<SKRectI>());

            var boxes = DecodePostProcessed(tensorOut, scaleX, scaleY, source.Width, source.Height);
            // Apply non-max suppression so overlapping detections of the
            // same face don't blur the same patch twice.
            boxes = NonMaxSuppression(boxes, NmsIou);
            return Task.FromResult<IReadOnlyList<SKRectI>>(boxes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OnnxFaceDetector: inference failed");
            return Task.FromResult<IReadOnlyList<SKRectI>>(Array.Empty<SKRectI>());
        }
    }

    /// <summary>
    /// Resize source → 320×320, convert to NCHW float32 with values 0..255
    /// (YuNet's expected normalisation — no mean/scale subtraction). Returns
    /// the tensor plus the per-axis scale factor needed to map detected
    /// coordinates back to source-image space.
    /// </summary>
    private static (DenseTensor<float> tensor, float scaleX, float scaleY) BuildInputTensor(SKBitmap source)
    {
        using var resized = source.Resize(new SKImageInfo(InputW, InputH), SKFilterQuality.Medium);
        var data = new float[1 * 3 * InputH * InputW];
        // SkiaSharp pixel accessor — BGRA8888 by default. YuNet expects BGR
        // (no alpha), so we drop the alpha channel and emit B, G, R planes
        // in NCHW layout.
        var pixels = resized.Pixels;       // SKColor[] of length W*H
        for (int y = 0; y < InputH; y++)
        {
            for (int x = 0; x < InputW; x++)
            {
                var c = pixels[y * InputW + x];
                int idxB = (0 * InputH + y) * InputW + x;
                int idxG = (1 * InputH + y) * InputW + x;
                int idxR = (2 * InputH + y) * InputW + x;
                data[idxB] = c.Blue;
                data[idxG] = c.Green;
                data[idxR] = c.Red;
            }
        }
        var tensor = new DenseTensor<float>(data, new[] { 1, 3, InputH, InputW });
        var scaleX = source.Width  / (float)InputW;
        var scaleY = source.Height / (float)InputH;
        return (tensor, scaleX, scaleY);
    }

    /// <summary>
    /// Decode YuNet's [N, 15] post-processed output:
    ///   col 0..3   bbox xywh (in 320-space)
    ///   col 4..13  5×(landmark x,y)  (unused)
    ///   col 14     confidence
    /// Detections below <see cref="ConfidenceThreshold"/> are dropped.
    /// </summary>
    private static List<SKRectI> DecodePostProcessed(
        Tensor<float> output, float sx, float sy, int srcW, int srcH)
    {
        var boxes = new List<SKRectI>();
        var dims = output.Dimensions;
        if (dims.Length != 2 || dims[1] < 5) return boxes;
        int n = dims[0];
        int width = dims[1];
        for (int i = 0; i < n; i++)
        {
            float conf = output[i, width - 1];
            if (conf < ConfidenceThreshold) continue;
            float x = output[i, 0] * sx;
            float y = output[i, 1] * sy;
            float w = output[i, 2] * sx;
            float h = output[i, 3] * sy;
            int left   = Math.Max(0, (int)x);
            int top    = Math.Max(0, (int)y);
            int right  = Math.Min(srcW, (int)(x + w));
            int bottom = Math.Min(srcH, (int)(y + h));
            if (right > left && bottom > top)
                boxes.Add(new SKRectI(left, top, right, bottom));
        }
        return boxes;
    }

    private static List<SKRectI> NonMaxSuppression(List<SKRectI> boxes, float iouThreshold)
    {
        var keep = new List<SKRectI>();
        var sorted = boxes
            .Select((b, i) => (b, area: (long)b.Width * b.Height))
            .OrderByDescending(x => x.area)
            .Select(x => x.b)
            .ToList();
        var suppressed = new bool[sorted.Count];
        for (int i = 0; i < sorted.Count; i++)
        {
            if (suppressed[i]) continue;
            keep.Add(sorted[i]);
            for (int j = i + 1; j < sorted.Count; j++)
            {
                if (suppressed[j]) continue;
                if (Iou(sorted[i], sorted[j]) > iouThreshold) suppressed[j] = true;
            }
        }
        return keep;
    }

    private static float Iou(SKRectI a, SKRectI b)
    {
        int x1 = Math.Max(a.Left, b.Left);
        int y1 = Math.Max(a.Top, b.Top);
        int x2 = Math.Min(a.Right, b.Right);
        int y2 = Math.Min(a.Bottom, b.Bottom);
        if (x2 <= x1 || y2 <= y1) return 0f;
        float inter = (x2 - x1) * (y2 - y1);
        float aArea = (float)a.Width * a.Height;
        float bArea = (float)b.Width * b.Height;
        return inter / (aArea + bArea - inter);
    }

    public void Dispose() => _session?.Dispose();
}
