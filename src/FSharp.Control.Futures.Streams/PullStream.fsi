namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


[<Struct>]
type StreamPoll<'a> =
    | Pending
    | Completed
    | Next of 'a

module StreamPoll =
    val inline map: mapper: ('a -> 'b) -> poll: StreamPoll<'a> -> StreamPoll<'b>

[<Interface>]
type IPullStream<'a> =
    abstract member PollNext: Waker -> StreamPoll<'a>

module PullStream =

    module Core =
        val inline pollNext: waker: Waker -> stream: IPullStream<'a> -> StreamPoll<'a>

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
    val bind: binder: ('a -> IPullStream<'b>) -> source: IPullStream<'a> -> IPullStream<'b>

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
