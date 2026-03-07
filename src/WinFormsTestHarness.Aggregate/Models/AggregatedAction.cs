using System.Text.Json.Serialization;

namespace WinFormsTestHarness.Aggregate.Models;

public class ClickAction
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("type")] public string Type => "Click";
    [JsonPropertyName("button")] public string Button { get; set; } = "Left";
    [JsonPropertyName("sx")] public int Sx { get; set; }
    [JsonPropertyName("sy")] public int Sy { get; set; }
    [JsonPropertyName("rx")] public int Rx { get; set; }
    [JsonPropertyName("ry")] public int Ry { get; set; }
}

public class DoubleClickAction
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("type")] public string Type => "DoubleClick";
    [JsonPropertyName("button")] public string Button { get; set; } = "Left";
    [JsonPropertyName("sx")] public int Sx { get; set; }
    [JsonPropertyName("sy")] public int Sy { get; set; }
    [JsonPropertyName("rx")] public int Rx { get; set; }
    [JsonPropertyName("ry")] public int Ry { get; set; }
}

public class RightClickAction
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("type")] public string Type => "RightClick";
    [JsonPropertyName("button")] public string Button => "Right";
    [JsonPropertyName("sx")] public int Sx { get; set; }
    [JsonPropertyName("sy")] public int Sy { get; set; }
    [JsonPropertyName("rx")] public int Rx { get; set; }
    [JsonPropertyName("ry")] public int Ry { get; set; }
}

public class DragAndDropAction
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("type")] public string Type => "DragAndDrop";
    [JsonPropertyName("startSx")] public int StartSx { get; set; }
    [JsonPropertyName("startSy")] public int StartSy { get; set; }
    [JsonPropertyName("startRx")] public int StartRx { get; set; }
    [JsonPropertyName("startRy")] public int StartRy { get; set; }
    [JsonPropertyName("endSx")] public int EndSx { get; set; }
    [JsonPropertyName("endSy")] public int EndSy { get; set; }
    [JsonPropertyName("endRx")] public int EndRx { get; set; }
    [JsonPropertyName("endRy")] public int EndRy { get; set; }
}

public class TextInputAction
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("type")] public string Type => "TextInput";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public class SpecialKeyAction
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("type")] public string Type => "SpecialKey";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("vk")] public int Vk { get; set; }
}

public class WheelScrollAction
{
    [JsonPropertyName("ts")] public string Ts { get; set; } = "";
    [JsonPropertyName("type")] public string Type => "WheelScroll";
    [JsonPropertyName("direction")] public string Direction { get; set; } = "";
    [JsonPropertyName("sx")] public int Sx { get; set; }
    [JsonPropertyName("sy")] public int Sy { get; set; }
    [JsonPropertyName("rx")] public int Rx { get; set; }
    [JsonPropertyName("ry")] public int Ry { get; set; }
}
