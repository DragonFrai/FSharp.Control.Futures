namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures


[<Struct; RequireQualifiedAccess>]
type StreamPoll<'a> =
    | Pending
    | Completed
    | Next of 'a

module StreamPoll =

    let inline map mapper poll =
        match poll with
        | StreamPoll.Next x -> StreamPoll.Next (mapper x)
        | StreamPoll.Pending -> StreamPoll.Pending
        | StreamPoll.Completed -> StreamPoll.Completed


/// # SeqStream pollNext schema
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x1 ] ->
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x2 ] ->
/// ...
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next xn ] ->
/// [ StreamPoll.Pending -> ...(may be inf))... -> StreamPoll.Pending ] -> StreamPoll.Completed -> ... -> StreamPoll.Completed
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

    let empty () = { new IPullStream<'a> with member _.PollNext(_) = StreamPoll.Completed }

    let single value =
        let mutable isCompleted = false
        Core.create ^fun _ ->
            if isCompleted
            then StreamPoll.Completed
            else
                isCompleted <- true
                StreamPoll.Next value

    /// Always returns SeqNext of the value
    let always value = Core.create ^fun _ -> StreamPoll.Next value

    let never () = Core.create ^fun _ -> StreamPoll.Pending

    let replicate count value =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        Core.create ^fun _ ->
            if current < count
            then
                current <- current + 1
                StreamPoll.Next value
            else StreamPoll.Completed

    let init count initializer =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        Core.create ^fun _ ->
            if current < count
            then
                let x = initializer current
                current <- current + 1
                StreamPoll.Next x
            else StreamPoll.Completed

    let initInfinite initializer =
        let mutable current = 0
        Core.create ^fun _ ->
            let x = initializer current
            current <- current + 1
            StreamPoll.Next x


    let ofSeq (src: 'a seq) : IPullStream<'a> =
        let enumerator = src.GetEnumerator()
        Core.create ^fun _ ->
            if enumerator.MoveNext()
            then StreamPoll.Next enumerator.Current
            else StreamPoll.Completed

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
                    | StreamPoll.Pending -> StreamPoll.Pending
                    | StreamPoll.Completed -> StreamPoll.Completed
                    | StreamPoll.Next x ->
                        let inners' = collector x
                        inners <- ValueSome inners'
                        loop ()
                | ValueSome inners' ->
                    let x = inners'.PollNext(waker)
                    match x with
                    | StreamPoll.Pending -> StreamPoll.Pending
                    | StreamPoll.Completed ->
                        inners <- ValueNone
                        loop ()
                    | StreamPoll.Next x -> StreamPoll.Next x
            loop ()

    /// Alias to `PullStream.collect`
    let inline bind binder source = collect binder source

    let iter (action: 'a -> unit) (source: IPullStream<'a>) : Future<unit> =
        Future.Core.create ^fun waker ->
            let rec loop () =
                match source.PollNext(waker) with
                | StreamPoll.Completed -> Poll.Ready ()
                | StreamPoll.Pending -> Poll.Pending
                | StreamPoll.Next x ->
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
                    | StreamPoll.Next x ->
                        let fut = action x
                        currFut <- ValueSome fut
                        loop ()
                    | StreamPoll.Pending -> Poll.Pending
                    | StreamPoll.Completed -> Poll.Ready ()
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
                | StreamPoll.Pending -> Poll.Pending
                | StreamPoll.Completed -> Poll.Ready currState
                | StreamPoll.Next x ->
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
                StreamPoll.Next currState
            else
                let sPoll = source.PollNext(waker)
                match sPoll with
                | StreamPoll.Pending -> StreamPoll.Pending
                | StreamPoll.Completed -> StreamPoll.Completed
                | StreamPoll.Next x ->
                    let state = folder currState x
                    currState <- state
                    StreamPoll.Next currState

    let chooseV (chooser: 'a -> 'b voption) (source: IPullStream<'a>) : IPullStream<'b> =
        Core.create ^fun waker ->
            let rec loop () =
                let sPoll = source.PollNext(waker)
                match sPoll with
                | StreamPoll.Pending -> StreamPoll.Pending
                | StreamPoll.Completed -> StreamPoll.Completed
                | StreamPoll.Next x ->
                    let r = chooser x
                    match r with
                    | ValueSome r -> StreamPoll.Next r
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
                | StreamPoll.Pending -> Poll.Pending
                | StreamPoll.Completed -> Poll.Ready ValueNone
                | StreamPoll.Next x ->
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

    let bufferByCount (bufferSize: int) (source: IPullStream<'a>) : IPullStream<'a[]> =
        let mutable buffer = Array.zeroCreate bufferSize
        let mutable currIdx = 0
        Core.create ^fun waker ->
            if obj.ReferenceEquals(buffer, null) then
                StreamPoll.Completed
            else
            let rec loop () =
                let p = source.PollNext(waker)
                match p with
                | StreamPoll.Pending -> StreamPoll.Pending
                | StreamPoll.Completed ->
                    let result = buffer.[0..currIdx]
                    buffer <- null
                    StreamPoll.Next result
                | StreamPoll.Next x ->
                    if currIdx >= bufferSize then
                        currIdx <- 0
                        StreamPoll.Next buffer
                    else
                        buffer.[currIdx] <- x
                        currIdx <- currIdx + 1
                        loop ()
            loop ()

    let filter (predicate: 'a -> bool) (source: IPullStream<'a>) : IPullStream<'a> =
        Core.create ^fun waker ->
            let rec loop () =
                let sPoll = source.PollNext(waker)
                match sPoll with
                | StreamPoll.Pending -> StreamPoll.Pending
                | StreamPoll.Completed -> StreamPoll.Completed
                | StreamPoll.Next x ->
                    if predicate x then
                        StreamPoll.Next x
                    else
                        loop ()
            loop ()

    let any (predicate: 'a -> bool) (source: IPullStream<'a>) : Future<bool> =
        let mutable result: bool voption = ValueNone
        Future.Core.create ^fun waker ->
            let rec loop () =
                match result with
                | ValueSome r ->
                    Poll.Ready r
                | ValueNone ->
                    let sPoll = source.PollNext(waker)
                    match sPoll with
                    | StreamPoll.Pending -> Poll.Pending
                    | StreamPoll.Completed ->
                        result <- ValueSome false
                        Poll.Ready false
                    | StreamPoll.Next x ->
                        if predicate x then
                            result <- ValueSome true
                            Poll.Ready true
                        else
                            loop ()
            loop ()

    let all (predicate: 'a -> bool) (source: IPullStream<'a>) : Future<bool> =
        let mutable result: bool voption = ValueNone
        Future.Core.create ^fun waker ->
            let rec loop () =
                match result with
                | ValueSome r -> Poll.Ready r
                | ValueNone ->
                    let sPoll = source.PollNext(waker)
                    match sPoll with
                    | StreamPoll.Pending -> Poll.Pending
                    | StreamPoll.Completed ->
                        result <- ValueSome true
                        Poll.Ready true
                    | StreamPoll.Next x ->
                        if predicate x then
                            loop ()
                        else
                            result <- ValueSome false
                            Poll.Ready false
            loop ()

    let zip (source1: IPullStream<'a>) (source2: IPullStream<'b>) : IPullStream<'a * 'b> =
        notImplemented "" // TODO: Implement, basing on Seq.zip behaviour, asynchronously

    let tryHeadV (source: IPullStream<'a>) : Future<'a voption> =
        Future.Core.memoizeReady ^fun waker ->
            match source.PollNext(waker) with
            | StreamPoll.Pending -> Poll.Pending
            | StreamPoll.Completed -> Poll.Ready ValueNone
            | StreamPoll.Next x -> Poll.Ready (ValueSome x)

    let tryLastV (source: IPullStream<'a>) : Future<'a voption> =
        Future.Core.memoizeReady ^fun waker ->
            match source.PollNext(waker) with
            | StreamPoll.Pending -> Poll.Pending
            | StreamPoll.Completed -> Poll.Ready ValueNone
            | StreamPoll.Next x -> Poll.Ready (ValueSome x)

    let ofFuture (fut: Future<'a>) : IPullStream<'a> =
        let mutable fut = fut // fut == null, when completed
        Core.create ^fun waker ->
            if obj.ReferenceEquals(fut, null) then
                StreamPoll.Completed
            else
                let p = fut.Poll(waker)
                match p with
                | Poll.Pending -> StreamPoll.Pending
                | Poll.Ready x ->
                    fut <- Unchecked.defaultof<_>
                    StreamPoll.Next x

    let inline singleAsync x = ofFuture x

    let delay (u2S: unit -> IPullStream<'a>) : IPullStream<'a> =
        let mutable _inner: IPullStream<'a> voption = ValueNone
        Core.create ^fun waker ->
            match _inner with
            | ValueNone ->
                let inner = u2S ()
                _inner <- ValueSome inner
                inner.PollNext(waker)
            | ValueSome inner -> inner.PollNext(waker)

    let take (count: int) (source: IPullStream<'a>) : IPullStream<'a> =
        let mutable _taken = 0
        Core.create ^fun waker ->
            if _taken >= count then
                StreamPoll.Completed
            else
            let p = source.PollNext(waker)
            match p with
            | StreamPoll.Pending -> StreamPoll.Pending
            | StreamPoll.Completed -> StreamPoll.Completed
            | StreamPoll.Next x ->
                _taken <- _taken + 1
                StreamPoll.Next x
