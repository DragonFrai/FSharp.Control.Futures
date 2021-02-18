namespace FSharp.Control.Futures


[<Struct>]
type SeqPoll<'a> =
    | SeqPending
    | SeqCompleted
    | SeqNext of 'a

[<AbstractClass>]
type SeqStream<'a> =
    new: unit -> SeqStream<'a>
    abstract member PollNext: Waker -> SeqPoll<'a>

module SeqStream =

    module Core =
        val inline pollNext: waker: Waker -> stream: SeqStream<'a> -> SeqPoll<'a>


    val ofSeq: src: 'a seq -> SeqStream<'a>
