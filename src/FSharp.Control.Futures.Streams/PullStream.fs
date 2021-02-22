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
type IPullStream<'a> =
    abstract member PollNext: Waker -> StreamPoll<'a>

[<RequireQualifiedAccess>]
module PullStream =

    [<RequireQualifiedAccess>]
    module Core =

        let inline create __expand_pollNext = { new IPullStream<_> with member _.PollNext(w) = __expand_pollNext w }

        let inline pollNext (waker: Waker) (stream: IPullStream<'a>) = stream.PollNext(waker)

    // -----------
    // Creation
    // -----------

    let empty () = { new IPullStream<'a> with member _.PollNext(_) = Completed }

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


    let ofSeq (src: 'a seq) : IPullStream<'a> =
        let enumerator = src.GetEnumerator()
        Core.create ^fun _ ->
            if enumerator.MoveNext()
            then Next enumerator.Current
            else Completed

    // -----------
    // Combinators
    // -----------

    let map (mapper: 'a -> 'b) (source: IPullStream<'a>) : IPullStream<'b> =
        Core.create ^fun waker -> source.PollNext(waker) |> StreamPoll.map mapper

    let collect (collector: 'a -> IPullStream<'b>) (source: IPullStream<'a>) : IPullStream<'b> =
        let mutable inners: IPullStream<'b> voption = ValueNone
        Core.create ^fun waker ->
            let rec loop () =
                match inners with
                | ValueNone ->
                    match source.PollNext(waker) with
                    | Pending -> Pending
                    | Completed -> Completed
                    | Next x ->
                        let inners' = collector x
                        inners <- ValueSome inners'
                        loop ()
                | ValueSome inners' ->
                    let x = inners'.PollNext(waker)
                    match x with
                    | Pending -> Pending
                    | Completed ->
                        inners <- ValueNone
                        loop ()
                    | Next x -> Next x
            loop ()

    /// Alias to `PullStream.collect`
    let bind binder source = collect binder source

    let iter (action: 'a -> unit) (source: IPullStream<'a>) : Future<unit> =
        Future.Core.create ^fun waker ->
            let rec loop () =
                match source.PollNext(waker) with
                | Completed -> Poll.Ready ()
                | Pending -> Poll.Pending
                | Next x ->
                    action x
                    loop ()
            loop ()

    let iterAsync (action: 'a -> Future<unit>) (source: IPullStream<'a>) : Future<unit> =
        let mutable currFut: Future<unit> voption = ValueNone
        Future.Core.create ^fun waker ->
            let rec loop () =
                match currFut with
                | ValueNone ->
                    let x = source.PollNext(waker)
                    match x with
                    | Next x ->
                        let fut = action x
                        currFut <- ValueSome fut
                        loop ()
                    | Pending -> Poll.Pending
                    | Completed -> Poll.Ready ()
                | ValueSome fut ->
                    let futPoll = fut.Poll(waker)
                    match futPoll with
                    | Poll.Ready () ->
                        currFut <- ValueNone
                        loop ()
                    | Poll.Pending -> Poll.Pending
            loop ()

    let fold (folder: 's -> 'a -> 's) (initState: 's) (source: IPullStream<'a>): Future<'s> =
        let mutable currState = initState
        Future.Core.create ^fun waker ->
            let rec loop () =
                let sPoll = source.PollNext(waker)
                match sPoll with
                | Pending -> Poll.Pending
                | Completed -> Poll.Ready currState
                | Next x ->
                    let state = folder currState x
                    currState <- state
                    loop ()
            loop ()

    let scan (folder: 's -> 'a -> 's) (initState: 's) (source: IPullStream<'a>) : IPullStream<'s> =
        let mutable currState = initState
        let mutable initReturned = false
        Core.create ^fun waker ->
            if not initReturned then
                initReturned <- true
                Next currState
            else
                let sPoll = source.PollNext(waker)
                match sPoll with
                | Pending -> Pending
                | Completed -> Completed
                | Next x ->
                    let state = folder currState x
                    currState <- state
                    Next currState

    let chooseV (chooser: 'a -> 'b voption) (source: IPullStream<'a>) : IPullStream<'b> =
        Core.create ^fun waker ->
            let rec loop () =
                let sPoll = source.PollNext(waker)
                match sPoll with
                | Pending -> Pending
                | Completed -> Completed
                | Next x ->
                    let r = chooser x
                    match r with
                    | ValueSome r -> Next r
                    | ValueNone -> loop ()
            loop ()

    let tryPickV (chooser: 'a -> 'b voption) (source: IPullStream<'a>) : Future<'b voption> =
        let mutable result: 'b voption = ValueNone
        Future.Core.create ^fun waker ->
            if result.IsSome then
                Poll.Ready result
            else
                let sPoll = source.PollNext(waker)
                match sPoll with
                | Pending -> Poll.Pending
                | Completed -> Poll.Ready ValueNone
                | Next x ->
                    let r = chooser x
                    match r with
                    | ValueNone -> Poll.Pending
                    | ValueSome r ->
                        result <- ValueSome r
                        Poll.Ready result

    let pickV (chooser: 'a -> 'b voption) (source: IPullStream<'a>) : Future<'b> =
        tryPickV chooser source
        |> Future.map ^function
            | ValueSome r -> r
            | ValueNone -> raise (System.Collections.Generic.KeyNotFoundException())

    let join (source: IPullStream<IPullStream<'a>>) : IPullStream<'a> =
        bind id source

    let append (source1: IPullStream<'a>) (source2: IPullStream<'a>) : IPullStream<'a> =
        join (ofSeq [ source1; source2 ])

//    let bufferByCount (bufferSize: int) (source: IPullStream<'a>) : IPullStream<'a[]> =
//        let buffer = Array.zeroCreate bufferSize
//        let mutable currIdx = 0
//
//        ()

    let filter (predicate: 'a -> bool) (source: IPullStream<'a>) : IPullStream<'a> =
        Core.create ^fun waker ->
            let rec loop () =
                let sPoll = source.PollNext(waker)
                match sPoll with
                | Pending -> Pending
                | Completed -> Completed
                | Next x ->
                    if predicate x then
                        Next x
                    else
                        loop ()
            loop ()

    let any (predicate: 'a -> bool) (source: IPullStream<'a>) : Future<bool> =
        let mutable result = ValueNone
        Future.Core.create ^fun waker ->
            let rec loop () =
                match result with
                | ValueSome r ->
                    Poll.Ready r
                | ValueNone ->
                    let sPoll = source.PollNext(waker)
                    match sPoll with
                    | Pending -> Poll.Pending
                    | Completed ->
                        result <- ValueSome false
                        Poll.Ready false
                    | Next x ->
                        if predicate x then
                            result <- ValueSome true
                            Poll.Ready true
                        else
                            loop ()
            loop ()

    let zip (source1: IPullStream<'a>) (source2: IPullStream<'b>) : IPullStream<'a * 'b> =
        notImplemented "" // TODO: Implement, basing on Seq.zip behaviour, asynchronously
