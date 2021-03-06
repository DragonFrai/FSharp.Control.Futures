namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


[<Struct; RequireQualifiedAccess>]
type StreamPoll<'a> =
    | Pending
    | Completed
    | Next of 'a

[<RequireQualifiedAccess>]
module StreamPoll =
    val inline map: mapper: ('a -> 'b) -> poll: StreamPoll<'a> -> StreamPoll<'b>

[<Interface>]
type IPullStream<'a> =
    abstract PollNext: Context -> StreamPoll<'a>
    abstract Cancel: unit -> unit

module PullStream =

    module Core =
        val inline create:
            __expand_pollNext: (Context -> StreamPoll<'a>) -> __expand_cancel: (unit -> unit) -> IPullStream<'a>
        val inline pollNext: context: Context -> stream: IPullStream<'a> -> StreamPoll<'a>
        val inline cancel: stream: IPullStream<'a> -> unit

    // --------
    // Creation
    // --------

    val empty: unit -> IPullStream<'a>

    val single: value: 'a -> IPullStream<'a>

    val always: value: 'a -> IPullStream<'a>

    val never: unit -> IPullStream<'a>

    val replicate: count: int -> value: 'a -> IPullStream<'a>

    val init: count: int -> initializer: (int -> 'a) -> IPullStream<'a>

    val initInfinite: initializer: (int -> 'a) -> IPullStream<'a>

    val ofSeq: src: 'a seq -> IPullStream<'a>

    // -----------
    // Compositors
    // -----------

    val map: mapper: ('a -> 'b) -> source: IPullStream<'a> -> IPullStream<'b>

    val collect: collector: ('a -> IPullStream<'b>) -> source: IPullStream<'a> -> IPullStream<'b>

    /// Alias to `PullStream.collect`
    val inline bind: binder: ('a -> IPullStream<'b>) -> source: IPullStream<'a> -> IPullStream<'b>

    val iter: action: ('a -> unit) -> source: IPullStream<'a> -> Future<unit>

    val iterAsync: action: ('a -> Future<unit>) -> source: IPullStream<'a> -> Future<unit>

    val fold: folder: ('s -> 'a -> 's) -> initState: 's -> source: IPullStream<'a> -> Future<'s>

    val scan: folder: ('s -> 'a -> 's) -> initState: 's -> source: IPullStream<'a> -> IPullStream<'s>

    val chooseV: chooser: ('a -> 'b voption) -> source: IPullStream<'a> -> IPullStream<'b>

    val tryPickV: chooser: ('a -> 'b voption) -> source: IPullStream<'a> -> Future<'b voption>

    val pickV: chooser: ('a -> 'b voption) -> source: IPullStream<'a> -> Future<'b>

    val join: source: IPullStream<IPullStream<'a>> -> IPullStream<'a>

    val append: source1: IPullStream<'a> -> source2: IPullStream<'a> -> IPullStream<'a>

    val filter: predicate: ('a -> bool) -> source: IPullStream<'a> -> IPullStream<'a>

    val any: predicate: ('a -> bool) -> source: IPullStream<'a> -> Future<bool>

    val all: predicate: ('a -> bool) -> source: IPullStream<'a> -> Future<bool>

    val zip: source1: IPullStream<'a> -> source2: IPullStream<'b> -> IPullStream<'a * 'b>

    val tryHeadV: source: IPullStream<'a> -> Future<'a voption>

    val tryLastV: source: IPullStream<'a> -> Future<'a voption>

    val ofFuture: fut: Future<'a> -> IPullStream<'a>

    /// Alias to `ofFuture`
    val inline singleAsync: x: Future<'a> -> IPullStream<'a>

    val delay: u2S: (unit -> IPullStream<'a>) -> IPullStream<'a>

    val take: count: int -> source: IPullStream<'a> -> IPullStream<'a>

    val bufferByCount: bufferSize: int -> source: IPullStream<'a> -> IPullStream<'a[]>
