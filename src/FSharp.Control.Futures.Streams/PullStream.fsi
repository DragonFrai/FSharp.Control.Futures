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

