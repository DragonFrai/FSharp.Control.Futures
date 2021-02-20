namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


[<Struct>]
type StreamPoll<'a> =
    | Pending
    | Completed
    | Next of 'a

module StreamPoll =

    let inline map mapper poll =
        match poll with
        | Next x -> Next (mapper x)
        | Pending -> Pending
        | Completed -> Completed


/// # SeqStream pollNext schema
/// [ [ Pending -> ...(may be inf)... -> Pending ] -> Next x1 ] ->
/// [ [ Pending -> ...(may be inf)... -> Pending ] -> Next x2 ] ->
/// ...
/// [ [ Pending -> ...(may be inf)... -> Pending ] -> Next xn ] ->
/// [ Pending -> ...(may be inf))... -> Pending ] -> Completed -> ... -> Completed
///
/// x1 != x2 != ... != xn
[<Interface>]
type IPollStream<'a> =
    abstract member PollNext: Waker -> StreamPoll<'a>

[<RequireQualifiedAccess>]
module PollStream =

    [<RequireQualifiedAccess>]
    module Core =

        let inline create __expand_pollNext = { new IPollStream<_> with member _.PollNext(w) = __expand_pollNext w }

        let inline pollNext (waker: Waker) (stream: IPollStream<'a>) = stream.PollNext(waker)

    // -----------
    // Creation
    // -----------

    let empty () = { new IPollStream<'a> with member _.PollNext(_) = Completed }

    let single value =
        let mutable isCompleted = false
        Core.create ^fun _ ->
            if isCompleted
            then Completed
            else
                isCompleted <- true
                Next value

    /// Always returns SeqNext of the value
    let always value = Core.create ^fun _ -> Next value

    let never () = Core.create ^fun _ -> Pending

    let replicate count value =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        Core.create ^fun _ ->
            if current < count
            then
                current <- current + 1
                Next value
            else Completed

    let init count initializer =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        Core.create ^fun _ ->
            if current < count
            then
                let x = initializer current
                current <- current + 1
                Next x
            else Completed

    let initInfinite initializer =
        let mutable current = 0
        Core.create ^fun _ ->
            let x = initializer current
            current <- current + 1
            Next x


    let ofSeq (src: 'a seq) : IPollStream<'a> =
        let enumerator = src.GetEnumerator()
        Core.create ^fun _ ->
            if enumerator.MoveNext()
            then Next enumerator.Current
            else Completed

    // -----------
    // Combinators
    // -----------

    let map (mapper: 'a -> 'b) (source: IPollStream<'a>) : IPollStream<'b> =
        Core.create ^fun waker -> source.PollNext(waker) |> StreamPoll.map mapper