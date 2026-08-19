# GossNet Observatory

Watch a gossip protocol flood a cluster.

The Observatory runs a real [GossNet.Protocol](https://github.com/gruberchris/GossNet.Protocol)
cluster over loopback UDP with every node's transport instrumented, then draws what is
normally invisible: which node heard a message from which, how long the cluster took to
converge, and how many datagrams it burned getting there.

It needs no changes to the library. `GossNetNode<T>` accepts an `IUdpClient`, so wrapping
the stock transport is enough to see both directions of every wire.

## The thing it shows

Same five nodes, same one message, two topologies:

```
╭─PROPAGATION  #29  reached 4/4 (100%)  in 0.4ms  cost 16 datagrams (amp 4.0x)─╮
│ n05 (origin)                                                                 │
│ ├── n01 +0.3ms                                                               │
│ ├── n02 +0.3ms                                                               │
│ ├── n03 +0.3ms                                                               │
│ └── n04 +0.3ms                                                               │
╰──────────────────────────────────────────────────────────────────────────────╯

╭─PROPAGATION  #29  reached 4/4 (100%)  in 0.9ms  cost 6 datagrams (amp 1.5x)──╮
│ n02 (origin)                                                                 │
│ ├── n01 +0.6ms                                                               │
│ │   └── n05 +0.9ms                                                           │
│ └── n03 +0.6ms                                                               │
│     └── n04 +0.9ms                                                           │
╰──────────────────────────────────────────────────────────────────────────────╯
```

A full mesh reaches everyone in one hop and spends 16 datagrams doing it. A ring relays,
takes twice as long, and spends 6. Everything else in the app exists to let you push on
that trade-off and watch what gives.

The full view adds a node grid that pulses as nodes accept, per-node counters (including
`dup`, the duplicates the protocol's cache absorbed), a convergence histogram, and an
activity log.

## Running it

```shell
dotnet run --project src/GossNet.Observatory -- --nodes 8 --topology ring
```

Then press `space`. Wants a console of at least 80x22; 120x44 is comfortable.

```shell
# a bigger, better-connected cluster, injecting continuously
dotnet run --project src/GossNet.Observatory -- --nodes 16 --topology krandom --auto

# headless measurement sweep
dotnet run --project src/GossNet.Observatory -- --bench --nodes 5,10,25 --loss 0,0.10
```

`--help` lists every option.

### Keys

| Key | Does |
| --- | --- |
| `space` | inject a message at a random live node |
| `1`–`9` | inject at a specific node |
| `←` `→` | move the selection |
| `k` / `r` | kill / revive the selected node |
| `p` | split the cluster in half, or heal it |
| `d` / `D` | decrease / increase packet loss |
| `l` / `L` | decrease / increase latency |
| `a` | toggle continuous injection |
| `t` / `f` | pin an earlier message / follow the latest |
| `q` | quit |

## What the numbers say

50 messages per combination, measured on loopback:

| nodes | loss | topology | p50 | p99 | dup ratio | amplification | coverage |
| ----: | ---: | -------- | --: | --: | --------: | ------------: | -------: |
| 5  | 0%  | mesh | 0.9ms | 14.0ms | 3.00 | 4.00  | 100% |
| 10 | 0%  | mesh | 3.3ms | 21.0ms | 8.00 | 9.00  | 100% |
| 25 | 0%  | mesh | 5.7ms | 18.0ms | 22.96 | 23.96 | 100% |
| 5  | 10% | mesh | 0.6ms | 17.8ms | 2.47 | 3.47  | 100% |
| 10 | 10% | mesh | 3.3ms | 19.3ms | 7.10 | 8.10  | 100% |
| 25 | 10% | mesh | 5.6ms | 20.1ms | 20.52 | 21.52 | 100% |
| 5  | 0%  | ring | 1.3ms | 39.7ms | 0.50 | 1.50  | 100% |
| 10 | 0%  | ring | 1.3ms | 46.6ms | 0.22 | 1.22  | 100% |
| 25 | 0%  | ring | 2.6ms | 23.2ms | 0.08 | 1.08  | 100% |
| 5  | 10% | ring | 0.9ms | 23.9ms | 0.35 | 1.35  | 94.5% |
| 10 | 10% | ring | 2.1ms | 27.5ms | 0.12 | 1.12  | 84.9% |
| 25 | 10% | ring | 10.4ms | 131.9ms | 0.02 | 1.02  | 54.3% |

Two things fall out of that:

- **A mesh costs exactly N−1 datagrams per node reached.** Amplification is 4.00, 9.00
  and 23.96 at 5, 10 and 25 nodes — the O(N²) flood, measured.
- **That waste is what buys resilience.** At 10% loss the mesh still reaches everyone,
  every time. The ring, which spends almost nothing, degrades badly: a single lost
  datagram severs an arm, and the longer the ring the likelier that is — coverage falls to
  54% at 25 nodes. Redundancy is not overhead; it is the delivery guarantee.

`amplification` is datagrams sent per node reached; `dup ratio` is duplicate datagrams
received per useful delivery; `coverage` is the fraction of other live nodes a message
actually got to.

## How it works

**Hop attribution.** `NotifiedNodes` travels inside each message, but it is a *set* — it
records who has seen a message, not who told whom. The edges come from the transport
instead: a datagram's source port is the sending node's own listening port, because that
is the socket it was bound to. First arrival wins; later copies are the duplicates the
protocol exists to suppress.

**Chaos.** Loss, latency, and partitions live in a `NetworkConditions` object that every
node's `ObservingUdpClient` consults per datagram. Killing a node calls `StopAsync()`
rather than disposing it, so its socket stays bound — datagrams sent while it is down
queue in the OS buffer and drain when you revive it.

Two details of the library shape that code, and are worth knowing if you write your own
transport decorator:

- **Never await inside `SendAsync`.** `GossNetNode.SocializeMessageAsync` holds a
  semaphore across every neighbour send, so a delay there serializes the node's entire
  fan-out — at 50ms and 20 neighbours, a one-second stall per message that looks exactly
  like a library bug. Simulated latency is therefore handed to `DelayedSendScheduler` and
  the call returns immediately.
- **A dropped datagram must report success.** The node only counts a neighbour as notified
  when the send returns a positive length. Real UDP gives a sender no delivery signal, so
  simulated loss returns the datagram length and records the drop out of band. Reporting
  failure would understate fan-out and misrepresent what loss does to this protocol.

## Layout

```
src/GossNet.Observatory/
├── Cluster/      node wiring, topologies, injection, kill/revive
├── Transport/    the IUdpClient decorator, network conditions, delayed sends
├── Telemetry/    event ingest, propagation tracking, metrics
├── Tui/          live view and panels
└── Bench/        headless sweeps
tests/            topology, tracker, metric and end-to-end cluster scenarios
```

`dotnet test` runs the lot, including scenarios that assert a mesh floods flat, a ring
relays, a killed node cuts the ring, and a partition holds.

## Building against GossNet.Protocol

This repo consumes `GossNet.Protocol` as a NuGet package, but **0.2.x is not on a public
feed yet**. The library's release workflow has been failing at the nuget.org push since
2026-08-16 with an expired `NUGET_API_KEY`, so nuget.org still stops at 0.1.16 and the
GitHub Packages push — which runs after it — never executed either. The 0.1.x API predates
everything the Observatory uses.

Until that is fixed, restore from a local folder feed. From a GossNet.Protocol checkout:

```shell
dotnet pack GossNet.Protocol/GossNet.Protocol.csproj -c Release \
  /p:Version=0.2.6-local.1 -o ../GossNet.Observatory/local-packages
```

`NuGet.config` maps `GossNet.*` to that folder. Once the key is rotated and `main`
publishes (GitVersion resolves it to **0.2.6**, not 0.2.0), change the one version line in
`Directory.Packages.props` and drop the `local` source.
