using System.Text;
using GossNet.Observatory.Cluster;
using GossNet.Observatory.Transport;

namespace GossNet.Observatory.Tests;

[TestClass]
public sealed class MessageIdReaderTests
{
    [TestMethod]
    public void ReadsTheIdOffAnActualSerializedMessage()
    {
        var message = new ObservatoryMessage { Origin = "n01", Seq = 7, Payload = "hello" };
        var datagram = Encoding.UTF8.GetBytes(message.Serialize());

        Assert.AreEqual(message.Id, MessageIdReader.TryRead(datagram));
    }

    [TestMethod]
    public void IgnoresNestedPropertiesNamedId()
    {
        // NotifiedNodes entries are objects one level down; a naive scan could pick a
        // property out of them instead of the message's own id.
        var id = Guid.NewGuid();
        var json = $$"""
            {"Nested":{"Id":"{{Guid.NewGuid()}}"},"Id":"{{id}}"}
            """;

        Assert.AreEqual(id, MessageIdReader.TryRead(Encoding.UTF8.GetBytes(json)));
    }

    [TestMethod]
    public void AcceptsLowerCaseId()
    {
        var id = Guid.NewGuid();

        Assert.AreEqual(id, MessageIdReader.TryRead(Encoding.UTF8.GetBytes($$"""{"id":"{{id}}"}""")));
    }

    [TestMethod]
    public void CorruptPayloadYieldsEmptyRatherThanThrowing()
    {
        Assert.AreEqual(Guid.Empty, MessageIdReader.TryRead("not json at all"u8));
        Assert.AreEqual(Guid.Empty, MessageIdReader.TryRead("{\"Id\":\"not-a-guid\"}"u8));
        Assert.AreEqual(Guid.Empty, MessageIdReader.TryRead([]));
    }

    [TestMethod]
    public void MissingIdYieldsEmpty()
    {
        Assert.AreEqual(Guid.Empty, MessageIdReader.TryRead("{\"Origin\":\"n01\"}"u8));
    }
}
