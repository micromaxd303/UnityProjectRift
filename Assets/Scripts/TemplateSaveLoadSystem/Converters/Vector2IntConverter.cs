using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class Vector2IntConverter : JsonConverter<Vector2Int>
{
    public override void WriteJson(JsonWriter writer, Vector2Int value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.x);
        writer.WritePropertyName("y");
        writer.WriteValue(value.y);
        writer.WriteEndObject();
    }

    public override Vector2Int ReadJson(JsonReader reader, Type objectType, Vector2Int existingValue,
        bool hasExistingValue, JsonSerializer serializer)
    {
        var obj = Newtonsoft.Json.Linq.JObject.Load(reader);
        return new Vector2Int(obj["x"].Value<int>(), obj["y"].Value<int>());
    }
}
