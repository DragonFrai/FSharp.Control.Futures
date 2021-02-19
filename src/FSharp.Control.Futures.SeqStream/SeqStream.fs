namespace FSharp.Control.Futures.SeqStream

open FSharp.Control.Futures


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


/// # SeqStream pollNext schema
/// [ [ SeqPending -> ...(may be infinite)... -> SeqPending ] -> SeqNext x1 ] ->
/// [ [ SeqPending -> ...(may be infinite)... -> SeqPending ] -> SeqNext x2 ] ->
/// ...
/// [ [ SeqPending -> ...(may be infinite)... -> SeqPending ] -> SeqNext xn ] ->
/// [ SeqPending -> ...(may be infinite)... -> SeqPending ] -> SeqCompleted -> ... -> SeqCompleted
///
/// x1 != x2 != ... != xn
[<Interface>]
type ISeqStream<'a> =
    abstract member PollNext: Waker -> SeqPoll<'a>

[<RequireQualifiedAccess>]
module SeqStream =

    [<RequireQualifiedAccess>]
    module Core =

        let inline create __expand_pollNext = { new ISeqStream<_> with member _.PollNext(w) = __expand_pollNext w }

        let inline pollNext (waker: Waker) (stream: ISeqStream<'a>) = stream.PollNext(waker)

    // -----------
    // Creation
    // -----------

    let empty () = { new ISeqStream<'a> with member _.PollNext(_) = SeqCompleted }

    let single value =
        let mutable isCompleted = false
        { new ISeqStream<_> with
            member _.PollNext(_) = if isCompleted then SeqCompleted else isCompleted <- true; SeqNext value }

    /// Always returns SeqNext of the value
    let always value = { new ISeqStream<'a> with member _.PollNext(_) = SeqNext value }

    let never () = { new ISeqStream<'a> with member _.PollNext(_) = SeqPending }

    let replicate count value =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        { new ISeqStream<'a> with
            member _.PollNext(_) =
                if current < count
                then
                    current <- current + 1
                    SeqNext value
                else SeqCompleted }

    let init count initializer =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        { new ISeqStream<'a> with
            member _.PollNext(_) =
                if current < count
                then
                    let x = initializer current
                    current <- current + 1
                    SeqNext x
                else SeqCompleted }

    let initInfinite initializer =
        let mutable current = 0
        { new ISeqStream<'a> with
            member _.PollNext(_) =
                let x = initializer current
                current <- current + 1
                SeqNext x }


    let ofSeq (src: 'a seq) : ISeqStream<'a> =
        let enumerator = src.GetEnumerator()
        { new ISeqStream<'a> with
            member this.PollNext(_) =
                if enumerator.MoveNext()
                then SeqNext enumerator.Current
                else SeqCompleted }

    // -----------
    // Combinators
    // -----------

    let map (mapper: 'a -> 'b) (source: ISeqStream<'a>) : ISeqStream<'b> =
        { new ISeqStream<'b> with member this.PollNext(waker) = source.PollNext(waker) |> SeqPoll.map mapper }
