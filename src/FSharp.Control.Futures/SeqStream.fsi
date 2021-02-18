namespace FSharp.Control.Futures


[<Struct>]
type SeqPoll<'a> =
    | SeqPending
    | SeqCompleted
    | SeqNext of 'a

module SeqPoll =
    val inline map: mapper: ('a -> 'b) -> poll: SeqPoll<'a> -> SeqPoll<'b>

[<AbstractClass>]
type SeqStream<'a> =
    new: unit -> SeqStream<'a>
    abstract member PollNext: Waker -> SeqPoll<'a>

module SeqStream =

    module Core =
        val inline pollNext: waker: Waker -> stream: SeqStream<'a> -> SeqPoll<'a>

    // --------
    // Creation
    // --------

    val empty: unit -> SeqStream<'a>

    val single: value: 'a -> SeqStream<'a>

    val always: value: 'a -> SeqStream<'a>

    val never: unit -> SeqStream<'a>

    val replicate: count: int -> value: 'a -> SeqStream<'a>

    val init: count: int -> initializer: (int -> 'a) -> SeqStream<'a>

    val initInfinite: initializer: (int -> 'a) -> SeqStream<'a>

    val ofSeq: src: 'a seq -> SeqStream<'a>

    // -----------
    // Compositors
    // -----------

    val map: mapper: ('a -> 'b) -> src: SeqStream<'a> -> SeqStream<'b>

