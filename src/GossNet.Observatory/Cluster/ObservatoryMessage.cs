using System.Text.Json;
using GossNet.Protocol;

namespace GossNet.Observatory.Cluster;

/// <summary>
/// The message flooded across the observed cluster.
/// </summary>
/// <remarks>
/// Deliberately small. The point of the demo is propagation, not payload, and every
/// byte here is multiplied by the fan-out.
/// </remarks>
internal sealed class ObservatoryMessage : GossNetMessageBase
{
    /// <summary>Gets or sets the display name of the node that first sent this message.</summary>
    public string Origin { get; set; } = string.Empty;

    /// <summary>Gets or sets the per-run sequence number, used as a short human-readable label.</summary>
    public int Seq { get; set; }

    /// <summary>Gets or sets filler used to study the effect of datagram size.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <inheritdoc />
    public override void Deserialize(string data)
    {
        // Restores Id, Timestamp and NotifiedNodes. Must run first: the protocol's own
        // metadata is what de-duplication and forwarding depend on.
        base.Deserialize(data);

        var message = JsonSerializer.Deserialize<ObservatoryMessage>(data);

        if (message is null)
        {
            return;
        }

        Origin = message.Origin;
        Seq = message.Seq;
        Payload = message.Payload;
    }
}
