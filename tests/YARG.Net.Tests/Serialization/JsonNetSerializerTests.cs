using System;
using System.Text.Json;
using YARG.Net.Serialization;
using Xunit;

namespace YARG.Net.Tests.Serialization;

public sealed class JsonNetSerializerTests
{
    [Fact]
    public void RoundTrip_WorksForRecordPayload()
    {
        var serializer = new JsonNetSerializer();
        var payload = new SampleRecord(42, "Test", SampleEnum.Second);

        var bytes = serializer.Serialize(payload);
        var clone = serializer.Deserialize<SampleRecord>(bytes.Span);

        Assert.Equal(payload, clone);
    }

    [Fact]
    public void DeserializeNullValueType_Throws()
    {
        var serializer = new JsonNetSerializer();
        var bytes = JsonSerializer.SerializeToUtf8Bytes("null");

        Assert.Throws<JsonException>(() => serializer.Deserialize<int>(bytes));
    }

    [Fact]
    public void DeserializeNullReference_ReturnsNull()
    {
        var serializer = new JsonNetSerializer();
        var bytes = JsonSerializer.SerializeToUtf8Bytes<string?>(null);

        var value = serializer.Deserialize<string?>(bytes);
        Assert.Null(value);
    }

    private sealed record SampleRecord(int Number, string Name, SampleEnum Mode);

    private enum SampleEnum
    {
        First,
        Second,
    }
}
