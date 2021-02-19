namespace FSharp.Control.Futures


[<Struct>]
type SeqPoll<'a> =
    | SeqPending
    | SeqCompleted
    | SeqNext of 'a

module SeqPoll =
    val inline map: mapper: ('a -> 'b) -> poll: SeqPoll<'a> -> SeqPoll<'b>

[<Interface>]
type ISeqStream<'a> =
    abstract member PollNext: Waker -> SeqPoll<'a>

module SeqStream =

    module Core =
        val inline pollNext: waker: Waker -> stream: ISeqStream<'a> -> SeqPoll<'a>

    // --------
    // Creation
    // --------

    val empty: unit -> ISeqStream<'a>

    val single: value: 'a -> ISeqStream<'a>

    val always: value: 'a -> ISeqStream<'a>

    val never: unit -> ISeqStream<'a>

    val replicate: count: int -> value: 'a -> ISeqStream<'a>

    val init: count: int -> initializer: (int -> 'a) -> ISeqStream<'a>

    val initInfinite: initializer: (int -> 'a) -> ISeqStream<'a>

    val ofSeq: src: 'a seq -> ISeqStream<'a>

    // -----------
    // Compositors
    // -----------

    val map: mapper: ('a -> 'b) -> src: ISeqStream<'a> -> ISeqStream<'b>

