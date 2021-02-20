namespace FSharp.Control.Futures.PollStream

open FSharp.Control.Futures


[<Struct>]
type StreamPoll<'a> =
    | Pending
    | Completed
    | Next of 'a

module StreamPoll =
    val inline map: mapper: ('a -> 'b) -> poll: StreamPoll<'a> -> StreamPoll<'b>

[<Interface>]
type IPollStream<'a> =
    abstract member PollNext: Waker -> StreamPoll<'a>

module PollStream =

    module Core =
        val inline pollNext: waker: Waker -> stream: IPollStream<'a> -> StreamPoll<'a>

    // --------
    // Creation
    // --------

    val empty: unit -> IPollStream<'a>

    val single: value: 'a -> IPollStream<'a>

    val always: value: 'a -> IPollStream<'a>

    val never: unit -> IPollStream<'a>

    val replicate: count: int -> value: 'a -> IPollStream<'a>

    val init: count: int -> initializer: (int -> 'a) -> IPollStream<'a>

    val initInfinite: initializer: (int -> 'a) -> IPollStream<'a>

    val ofSeq: src: 'a seq -> IPollStream<'a>

    // -----------
    // Compositors
    // -----------

    val map: mapper: ('a -> 'b) -> source: IPollStream<'a> -> IPollStream<'b>

