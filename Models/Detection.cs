using System.Text.Json.Serialization;

namespace ModelHotSwapWorkflow.Models
{
    public class DetectionResult
    {
        public System.Drawing.Image Image { get; set; }
        public System.Collections.Generic.List<Detection> Detections { get; set; }
        public double Confidence { get; set; }
        public string ModelName { get; set; }
    }

    public class Detection
    {
        [JsonPropertyName("bbox")]
        public float[] Bbox { get; set; } = System.Array.Empty<float>();

        [JsonPropertyName("confidence")]
        public float Confidence { get; set; }

        [JsonPropertyName("class_id")]
        public int ClassId { get; set; }
    }
}