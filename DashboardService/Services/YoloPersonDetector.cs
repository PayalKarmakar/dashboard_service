using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace DashboardService.Services;

public sealed class YoloPersonDetector : IDisposable
{
    private readonly Net _net;
    private readonly double _minConfidence;
    private readonly int _inputSize;
    private const int FeatureCount = 85; // x,y,w,h,obj + 80 COCO classes

    public YoloPersonDetector(string modelPath, double minConfidence, int inputSize = 320)
    {
        if (!File.Exists(modelPath))
        {
            throw new FileNotFoundException("YOLO model not found.", modelPath);
        }

        _minConfidence = Math.Clamp(minConfidence, 0.1, 0.95);
        _inputSize = inputSize <= 0 ? 320 : inputSize;
        _net = CvDnn.ReadNetFromOnnx(modelPath)
            ?? throw new InvalidOperationException("Failed to load YOLO ONNX model.");
        _net.SetPreferableBackend(Backend.OPENCV);
        _net.SetPreferableTarget(Target.CPU);
    }

    public IReadOnlyList<PersonDetection> Detect(Mat frame)
    {
        if (frame.Empty())
        {
            return [];
        }

        try
        {
            using var blob = CvDnn.BlobFromImage(
                frame,
                scaleFactor: 1.0 / 255.0,
                size: new Size(_inputSize, _inputSize),
                mean: new Scalar(),
                swapRB: true,
                crop: false);

            _net.SetInput(blob);
            using var output = _net.Forward();
            return ParseDetections(output, frame.Width, frame.Height);
        }
        catch
        {
            return [];
        }
    }

    private List<PersonDetection> ParseDetections(Mat output, int frameWidth, int frameHeight)
    {
        if (output.Empty())
        {
            return [];
        }

        float[]? data = TryCopyFloats(output);
        if (data == null || data.Length < FeatureCount)
        {
            return [];
        }

        // Layout detection for YOLOv5-style ONNX:
        // [1, N, 85]  -> row-major proposals
        // [1, 85, N]  -> transposed features
        bool transposed = false;
        int proposalCount = data.Length / FeatureCount;

        if (output.Dims >= 3)
        {
            int dim1 = output.Size(1);
            int dim2 = output.Size(2);
            if (dim1 == FeatureCount && dim2 > FeatureCount)
            {
                transposed = true;
                proposalCount = dim2;
            }
            else if (dim2 == FeatureCount && dim1 > 1)
            {
                transposed = false;
                proposalCount = dim1;
            }
        }

        if (proposalCount <= 0 || proposalCount * FeatureCount > data.Length)
        {
            proposalCount = data.Length / FeatureCount;
            transposed = false;
        }

        float scaleX = (float)frameWidth / _inputSize;
        float scaleY = (float)frameHeight / _inputSize;

        var boxes = new List<Rect>();
        var scores = new List<float>();

        for (int i = 0; i < proposalCount; i++)
        {
            float cx;
            float cy;
            float w;
            float h;
            float objectness;
            float personClass;

            if (transposed)
            {
                cx = data[0 * proposalCount + i];
                cy = data[1 * proposalCount + i];
                w = data[2 * proposalCount + i];
                h = data[3 * proposalCount + i];
                objectness = data[4 * proposalCount + i];
                personClass = data[5 * proposalCount + i];
            }
            else
            {
                int offset = i * FeatureCount;
                if (offset + 5 >= data.Length)
                {
                    break;
                }

                cx = data[offset];
                cy = data[offset + 1];
                w = data[offset + 2];
                h = data[offset + 3];
                objectness = data[offset + 4];
                personClass = data[offset + 5];
            }

            if (objectness < _minConfidence)
            {
                continue;
            }

            float personScore = objectness * personClass;
            if (personScore < _minConfidence)
            {
                continue;
            }

            int x = (int)((cx - w / 2f) * scaleX);
            int y = (int)((cy - h / 2f) * scaleY);
            int width = (int)(w * scaleX);
            int height = (int)(h * scaleY);

            if (width <= 1 || height <= 1)
            {
                continue;
            }

            x = Math.Clamp(x, 0, Math.Max(0, frameWidth - 1));
            y = Math.Clamp(y, 0, Math.Max(0, frameHeight - 1));
            width = Math.Clamp(width, 1, Math.Max(1, frameWidth - x));
            height = Math.Clamp(height, 1, Math.Max(1, frameHeight - y));

            boxes.Add(new Rect(x, y, width, height));
            scores.Add(personScore);
        }

        if (boxes.Count == 0)
        {
            return [];
        }

        int[] indices = SoftNms(boxes, scores);
        var results = new List<PersonDetection>(indices.Length);
        foreach (int index in indices)
        {
            results.Add(new PersonDetection
            {
                Box = boxes[index],
                Confidence = Math.Clamp(scores[index] * 100f, 0f, 100f)
            });
        }

        return results;
    }

    /// <summary>
    /// Copies DNN output floats without Mat.GetArray/Reshape (those throw on 3D/4D mats).
    /// </summary>
    private static float[]? TryCopyFloats(Mat output)
    {
        try
        {
            using var continuous = output.IsContinuous() ? output : output.Clone();
            long total = continuous.Total() * continuous.Channels();
            if (total <= 0 || total > int.MaxValue)
            {
                return null;
            }

            var data = new float[(int)total];
            if (continuous.Type() == MatType.CV_32FC1 ||
                continuous.Type() == MatType.CV_32F ||
                continuous.Depth() == MatType.CV_32F)
            {
                Marshal.Copy(continuous.Data, data, 0, data.Length);
                return data;
            }

            using var floated = new Mat();
            continuous.ConvertTo(floated, MatType.CV_32FC1);
            using var flat = floated.IsContinuous() ? floated : floated.Clone();
            Marshal.Copy(flat.Data, data, 0, data.Length);
            return data;
        }
        catch
        {
            return null;
        }
    }

    private int[] SoftNms(List<Rect> boxes, List<float> scores)
    {
        try
        {
            CvDnn.NMSBoxes(boxes, scores, (float)_minConfidence, 0.45f, out int[] indices);
            if (indices is { Length: > 0 })
            {
                return indices;
            }
        }
        catch
        {
            // Manual fallback below.
        }

        return scores
            .Select((score, index) => (score, index))
            .OrderByDescending(x => x.score)
            .Take(Math.Min(10, scores.Count))
            .Select(x => x.index)
            .ToArray();
    }

    public void Dispose()
    {
        _net.Dispose();
    }
}

public sealed class PersonDetection
{
    public Rect Box { get; init; }

    public float Confidence { get; init; }
}
