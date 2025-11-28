using Newtonsoft.Json;
using YARG.Net.Serialization;
using Xunit;

namespace YARG.Net.Tests.Serialization;

public sealed class NewtonsoftNetSerializerTests
{
    [Fact]
    public void RoundTrip_WorksForRecordPayload()
    {
        var serializer = new NewtonsoftNetSerializer();
        var payload = new SampleRecord(7, "Unity", SampleEnum.Second);

        var bytes = serializer.Serialize(payload);
        var clone = serializer.Deserialize<SampleRecord>(bytes.Span);

        Assert.Equal(payload, clone);
    }

    [Fact]
    public void DeserializeNullValueType_Throws()
    {
        var serializer = new NewtonsoftNetSerializer();
        var bytes = System.Text.Encoding.UTF8.GetBytes("null");

        Assert.Throws<JsonSerializationException>(() => serializer.Deserialize<int>(bytes));
    }

    [Fact]
    public void DeserializeNullReference_ReturnsNull()
    {
        var serializer = new NewtonsoftNetSerializer();
        var bytes = System.Text.Encoding.UTF8.GetBytes("null");

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
