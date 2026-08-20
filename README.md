# GossNet Observatory

Watch a gossip protocol flood a cluster.

The Observatory runs a real [GossNet.Protocol](https://github.com/gruberchris/GossNet.Protocol)
cluster over loopback UDP with every node's transport instrumented, then draws what is
normally invisible: which node heard a message from which, how long the cluster took to
converge, and how many datagrams it burned getting there.

It needs no changes to the library. `GossNetNode<T>` accepts an `IUdpClient`, so wrapping
the stock transport is enough to see both directions of every wire.

![A 9-node krandom cluster: messages injected and traced hop by hop, a node killed and
routed around, then revived under continuous injection](docs/demo.gif)

<sup>Recorded with [vhs](https://github.com/charmbracelet/vhs); regenerate with
`vhs docs/demo.tape`.</sup>

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

### Topologies

`--topology` decides who each node gossips with. It is the single biggest lever in the
app: the protocol is identical in every case, only the neighbour lists differ. Each node's
list becomes its `StaticNodes`, and every graph is symmetric — if A lists B, B lists A.

| Value | Shape | Neighbours per node (at 25) |
| --- | --- | --- |
| `mesh` | every node knows every other node | 24 |
| `ring` | each node knows only the node either side of it | 2 |
| `krandom` | a ring, plus random shortcuts across it | 4–9, avg 6 |
| `grid` | a square lattice, four-connected | 2–4, avg 3.2 |

- **`mesh`** — one hop to everyone, and the O(N²) bill that comes with it. Every recipient
  still forwards to every neighbour not already listed in `NotifiedNodes`, so a 25-node
  cluster spends ~24 datagrams per node reached. Produces a completely flat tree.
- **`ring`** — the cheapest graph that still connects everything: ~1 datagram per node
  reached. The message crawls both ways around the circle, so the tree is a pair of long
  chains and the diameter is N/2. Also the most fragile: one lost datagram severs an arm.
- **`krandom`** — a ring backbone guarantees connectivity, then each node adds up to
  `--degree` random chords (default 2) that cut the diameter to roughly log N. `--seed`
  makes the wiring reproducible. The default topology to reach for when you want realistic
  gossip behaviour.
- **`grid`** — nodes laid out in a `ceil(sqrt(N))`-wide lattice linked right and down.
  Propagation spreads as a visible wavefront, and the diameter is about 2·sqrt(N). Corner
  and edge nodes have fewer links than interior ones, so it is the only built-in topology
  with genuinely uneven connectivity.

Measured at 25 nodes, 40 messages each — the trade-off is monotonic:

| topology | datagrams per node reached | coverage at 10% loss |
| --- | --: | --: |
| `mesh` | 23.92 | 100% |
| `krandom` | 5.25 | 100% |
| `grid` | 2.33 | 99.5% |
| `ring` | 1.08 | **62.7%** |

`krandom` is the interesting row: full delivery under 10% loss for a fifth of a mesh's
traffic. That is the property real gossip systems are built on — you do not need everyone
to know everyone, you need enough redundant paths that losing some doesn't disconnect
anyone.

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

- **Never await inside `SendAsync`.** The node awaits its whole fan-out before a send —
  or, on the receive path, a forward — counts as complete. (Before GossNet.Protocol
  0.10.0 the sends were also serialized behind a semaphore; they are parallel now, but
  still awaited.) A delay inside `SendAsync` would therefore stall every sender and the
  node's processing loop for the full latency on each message, which looks exactly like
  a library bug. Simulated latency is handed to `DelayedSendScheduler` instead, and the
  call returns immediately.
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

Restores `GossNet.Protocol` **0.10.0** from nuget.org — nothing local required:

```shell
dotnet restore
```

**0.3.0 is the minimum this app can build against.** Anything older predates
`INodeDiscovery`, per-subscriber subscriptions, and the cancellable `IUdpClient` the
instrumented transport depends on. (There is no 0.2.x on any feed at all: that work was
numbered 0.2.0 while the library's publishing pipeline was broken, and shipped as 0.3.0 once
fixed.)

The Observatory deliberately uses only `NodeDiscovery.StaticList`, because hand-built
neighbour lists are what let it shape mesh, ring, k-random and grid topologies — something no
registry-backed provider can do. Everything the library has added since 0.3.0 (peer exchange,
multicast, composite, the cloud and registry providers, watch support) is therefore unused
here, and upgrading needs no code changes.

To try the app against uncommitted library changes, pack the library locally and point a
folder feed at it:

```shell
dotnet pack GossNet.Protocol/GossNet.Protocol.csproj -c Release \
  /p:Version=0.10.0-local.1 -o ../GossNet.Observatory/local-packages
```

then add that folder as a source and bump the version in `Directory.Packages.props`. With
more than one source configured, Central Package Management requires explicit
`packageSourceMapping` entries or restore fails with NU1507.
