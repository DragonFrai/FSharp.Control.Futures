namespace FSharp.Control.Futures


[<Struct>]
type SeqPoll<'a> =
    | SeqPending
    | SeqCompleted
    | SeqNext of 'a

module SeqPoll =

    let inline map mapper poll =
        match poll with
        | SeqNext x -> SeqNext (mapper x)
        | SeqPending -> SeqPending
        | SeqCompleted -> SeqCompleted


[<AbstractClass>]
type SeqStream<'a>() =
    abstract member PollNext: Waker -> SeqPoll<'a>

module SeqStream =

    module Core =
        let inline pollNext (waker: Waker) (stream: SeqStream<'a>) = stream.PollNext(waker)

    // -----------
    // Creation
    // -----------

    let empty<'a> () = { new SeqStream<'a>() with member _.PollNext(_w) = SeqCompleted }

    let single value =
        let mutable isCompleted = false
        { new SeqStream<_>() with
            member _.PollNext(_w) = if isCompleted then SeqCompleted else isCompleted <- true; SeqNext value }

    let always value = { new SeqStream<'a>() with member _.PollNext(_w) = SeqNext value }

    let never<'a> () = { new SeqStream<'a>() with member _.PollNext(_w) = SeqPending }

    let replicate count value =
        if count < 0 then invalidOp "count < 0"
        let mutable current = 0
        { new SeqStream<'a>() with
            member _.PollNext(_w) =
                if current < count
                then current <- current + 1; SeqNext value
                else SeqCompleted }

    let init count initializer =
        if count < 0 then invalidOp "count < 0"
        let mutable current = 0
        { new SeqStream<'a>() with
            member _.PollNext(_w) =
                if current < count
                then
                    let x = initializer current
                    current <- current + 1
                    SeqNext x
                else SeqCompleted }

    let initInfinite initializer =
        let mutable current = 0
        { new SeqStream<'a>() with
            member _.PollNext(_w) =
                let x = initializer current
                current <- current + 1
                SeqNext x }


    let ofSeq (src: 'a seq) : SeqStream<'a> =
        let enumerator = src.GetEnumerator()
        { new SeqStream<'a>() with
            member this.PollNext(_waker) =
                if enumerator.MoveNext()
                then SeqNext enumerator.Current
                else SeqCompleted }

    // -----------
    // Combinators
    // -----------

    let map (mapper: 'a -> 'b) (src: SeqStream<'a>) : SeqStream<'b> =
        { new SeqStream<'b>() with member this.PollNext(waker) = src.PollNext(waker) |> SeqPoll.map mapper }
