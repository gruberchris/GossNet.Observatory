using System.Text.Json;

namespace GossNet.Observatory.Transport;

/// <summary>
/// Pulls the protocol message id out of a serialized datagram.
/// </summary>
/// <remarks>
/// A targeted scan rather than a full deserialize: this runs on every datagram in both
/// directions, and the transport has no business materializing the application's
/// message type just to label an event.
/// </remarks>
internal static class MessageIdReader
{
    /// <summary>
    /// Reads the top-level <c>Id</c> property.
    /// </summary>
    /// <param name="utf8Json">The serialized message.</param>
    /// <returns>The id, or <see cref="Guid.Empty"/> when the payload is not readable.</returns>
    public static Guid TryRead(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);

            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName || reader.CurrentDepth != 1)
                {
                    continue;
                }

                // GossNetMessageBase serializes with no naming policy, so the property is
                // "Id"; the lower-case form is tolerated in case that ever changes.
                if (!reader.ValueTextEquals("Id"u8) && !reader.ValueTextEquals("id"u8))
                {
                    continue;
                }

                return reader.Read() && reader.TryGetGuid(out var id) ? id : Guid.Empty;
            }
        }
        catch (JsonException)
        {
            // A truncated or corrupt datagram is a fact about the network, not an error
            // the observatory should crash on.
        }

        return Guid.Empty;
    }
}
