using System;
using Newtonsoft.Json;

namespace VolumeControl
{
    public class FloatFormatConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(float);

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) =>
            writer.WriteRawValue($"{value:0.00}");

        public override bool CanRead => false;

        public override object ReadJson(
            JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) =>
            throw new NotImplementedException();
    }
}
