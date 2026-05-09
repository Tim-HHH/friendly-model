using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HMManager
{
    public class EnumConverterInner<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            // 第一种情况：如果咱们直接写了文字，比如 "Detection" （变聪明了，能直接认出来！）
            if (reader.TokenType == JsonTokenType.String)
            {
                string enumStr = reader.GetString();
                if (Enum.TryParse(enumStr, true, out T result)) return result;
            }
            // 第二种情况：如果是用盒子装起来的旧格式，比如 {"value": "Detection"}
            else if (reader.TokenType == JsonTokenType.StartObject)
            {
                string enumValue = null;
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        if (reader.GetString() == "value")
                        {
                            reader.Read();
                            if (reader.TokenType == JsonTokenType.String) enumValue = reader.GetString();
                        }
                    }
                }
                if (enumValue != null && Enum.TryParse(enumValue, true, out T result)) return result;
            }

            // 如果实在看不懂，再报错
            throw new JsonException($"看不懂这个任务类型啦: {typeToConvert.Name}");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("value", value.ToString());
            writer.WriteEndObject();
        }
    }

    public class ModelConfigCollection : List<ModelConfig> { }

    public class ModelConfig
    {
        [JsonPropertyName("model_path")]
        public string ModelPath { get; set; } = string.Empty;

        [JsonPropertyName("task_type")]
        [JsonConverter(typeof(EnumConverterInner<TaskType>))]
        public TaskType TaskType { get; set; } = TaskType.Detection;

        [JsonPropertyName("input_config")]
        public InputConfig InputConfig { get; set; } = new InputConfig();

        [JsonPropertyName("output_config")]
        public List<OutputConfig> OutputConfig { get; set; } = new List<OutputConfig>() { new OutputConfig() };

        [JsonPropertyName("class_definitions")]
        public List<ClassDefinition> ClassDefinitions { get; set; } = new List<ClassDefinition>() { new ClassDefinition() };

        [JsonPropertyName("performance_settings")]
        public PerformanceSettings PerformanceSettings { get; set; } = new PerformanceSettings();
    }

    public enum TaskType { Detection, Segmentation, Classification }

    public class InputConfig
    {
        [JsonPropertyName("input_width")] public int Width { get; set; } = 640;
        [JsonPropertyName("input_height")] public int Height { get; set; } = 640;
        [JsonPropertyName("color_channels")] public int Channels { get; set; } = 3;
    }

    public class OutputConfig
    {
        [JsonPropertyName("class_id")] public int Id { get; set; } = 0;
        [JsonPropertyName("min_confidence")] public float ConfidenceThreshold { get; set; } = 0.25f;
        [JsonPropertyName("nms_iou_threshold")] public float IouThreshold { get; set; } = 0.45f;
        [JsonPropertyName("max_detections_per_image")] public int MaxDetections { get; set; } = 300;
    }

    public class ClassDefinition
    {
        [JsonPropertyName("class_id")] public int Id { get; set; } = 0;
        [JsonPropertyName("class_name")] public string Name { get; set; } = "unlabeled";
        [JsonPropertyName("display_color")] public ColorRGB DisplayColor { get; set; } = new ColorRGB(255, 0, 0);
        [JsonIgnore] public string HexColor => DisplayColor.ToHex();
    }

    public struct ColorRGB
    {
        [JsonPropertyName("r")] public byte R { get; set; }
        [JsonPropertyName("g")] public byte G { get; set; }
        [JsonPropertyName("b")] public byte B { get; set; }
        public ColorRGB(byte r, byte g, byte b) => (R, G, B) = (r, g, b);
        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
    }

    public class PerformanceSettings
    {
        [JsonPropertyName("enable_gpu_acceleration")] public bool UseGpu { get; set; } = true;
        [JsonPropertyName("gpu_device_id")] public int GpuId { get; set; } = 0;
        [JsonPropertyName("warmup_iterations")] public int WarmupRounds { get; set; } = 1;
        [JsonPropertyName("cpu_threads")] public int Threads { get; set; } = Environment.ProcessorCount / 2;
    }
}