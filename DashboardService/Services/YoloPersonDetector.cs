using System.IO;
using OpenCvSharp;
using OpenCvSharp.Dnn;

namespace DashboardService.Services;

public sealed class YoloPersonDetector : IDisposable
{
    private readonly Net _net;
    private readonly double _minConfidence;
    private readonly int _inputSize;

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

    private List<PersonDetection> ParseDetections(Mat output, int frameWidth, int frameHeight)
    {
        // YOLOv5 ONNX output is typically [1, N, 85]
        Mat detections;
        if (output.Dims == 3)
        {
            int rows = output.Size(1);
            detections = output.Reshape(1, rows);
        }
        else
        {
            detections = output;
        }

        float scaleX = (float)frameWidth / _inputSize;
        float scaleY = (float)frameHeight / _inputSize;

        var boxes = new List<Rect>();
        var scores = new List<float>();

        int rowCount = detections.Rows;
        for (int i = 0; i < rowCount; i++)
        {
            float objectness = detections.At<float>(i, 4);
            if (objectness < _minConfidence)
            {
                continue;
            }

            // class 0 = person
            float personScore = detections.At<float>(i, 5) * objectness;
            if (personScore < _minConfidence)
            {
                continue;
            }

            float cx = detections.At<float>(i, 0);
            float cy = detections.At<float>(i, 1);
            float w = detections.At<float>(i, 2);
            float h = detections.At<float>(i, 3);

            int x = (int)((cx - w / 2f) * scaleX);
            int y = (int)((cy - h / 2f) * scaleY);
            int width = (int)(w * scaleX);
            int height = (int)(h * scaleY);

            x = Math.Clamp(x, 0, frameWidth - 1);
            y = Math.Clamp(y, 0, frameHeight - 1);
            width = Math.Clamp(width, 1, frameWidth - x);
            height = Math.Clamp(height, 1, frameHeight - y);

            boxes.Add(new Rect(x, y, width, height));
            scores.Add(personScore);
        }

        if (boxes.Count == 0)
        {
            return [];
        }

        CvDnn.NMSBoxes(boxes, scores, (float)_minConfidence, 0.45f, out int[] indices);

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
