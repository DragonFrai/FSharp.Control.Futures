namespace FSharp.Control.Futures


[<Struct>]
type SeqPoll<'a> =
    | SeqPending
    | SeqCompleted
    | SeqNext of 'a

[<AbstractClass>]
type SeqStream<'a>() =
    abstract member PollNext: Waker -> SeqPoll<'a>

module SeqStream =

    module Core =
        let inline pollNext (waker: Waker) (stream: SeqStream<'a>) = stream.PollNext(waker)

    let ofSeq (src: 'a seq) : SeqStream<'a> =
        let enumerator = src.GetEnumerator()
        { new SeqStream<'a>() with
            member this.PollNext(_waker) =
                if enumerator.MoveNext()
                then SeqNext enumerator.Current
                else SeqCompleted }

