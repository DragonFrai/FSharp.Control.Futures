namespace FSharp.Control.Futures.Streams

open System
open System.IO
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
    abstract PollNext: Context -> StreamPoll<'a>
    abstract Cancel: unit -> unit

exception StreamCancelledException

[<RequireQualifiedAccess>]
module PullStream =

    [<RequireQualifiedAccess>]
    module Core =

        let inline create (__expand_pollNext: Context -> StreamPoll<'a>) (__expand_cancel: unit -> unit) =
            { new IPullStream<_> with
                member _.PollNext(ctx) = __expand_pollNext ctx
                member _.Cancel() = __expand_cancel () }

        let inline cancel (stream: IPullStream<'a>) =
            stream.Cancel()

        let inline pollNext (context: Context) (stream: IPullStream<'a>) = stream.PollNext(context)

    // -----------
    // Creation
    // -----------

    let empty () =
        Core.create
        <| fun _ -> StreamPoll.Completed
        <| fun () -> do ()

    [<Struct>]
    type SingleState<'a> =
        | Value of 'a
        | Completed
        | Cancelled

    let single value =
        let mutable state = SingleState.Value value
        Core.create
        <| fun _ ->
            match state with
            | Value x ->
                state <- Completed
                StreamPoll.Next x
            | Completed -> StreamPoll.Completed
            | Cancelled -> raise StreamCancelledException
        <| fun () ->
            match state with
            | Value x -> state <- Cancelled
            | Completed -> state <- Cancelled
            | Cancelled -> invalidOp "Double cancel"

    /// Always returns SeqNext of the value
    let always value =
        Core.create
        <| fun _ -> StreamPoll.Next value
        <| fun () -> do ()

    let never () =
        Core.create
        <| fun _ -> StreamPoll.Pending
        <| fun () -> do ()

    let replicate count value =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        Core.create
        <| fun _ ->
            if current < count
            then
                current <- current + 1
                StreamPoll.Next value
            else StreamPoll.Completed
        <| fun () ->
            current <- count

    let init count initializer =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        Core.create
        <| fun _ ->
            if current < count
            then
                let x = initializer current
                current <- current + 1
                StreamPoll.Next x
            else StreamPoll.Completed
        <| fun () ->
            current <- count

    let initInfinite initializer =
        let mutable current = 0
        Core.create
        <| fun _ ->
            let x = initializer current
            current <- current + 1
            StreamPoll.Next x
        <| fun () ->
            do ()

    let ofSeq (src: 'a seq) : IPullStream<'a> =
        let mutable enumerator = src.GetEnumerator()
        Core.create
        <| fun _ ->
            if enumerator.MoveNext()
            then StreamPoll.Next enumerator.Current
            else StreamPoll.Completed
        <| fun () -> enumerator <- Unchecked.defaultof<_>


    // -----------
    // Combinators
    // -----------

    let map (mapper: 'a -> 'b) (source: IPullStream<'a>) : IPullStream<'b> =
        Core.create
        <| fun context -> source.PollNext(context) |> StreamPoll.map mapper
        <| fun () -> do source.Cancel()

    let collect (collector: 'a -> IPullStream<'b>) (source: IPullStream<'a>) : IPullStream<'b> =
        let mutable source = source
        let mutable inners: IPullStream<'b> voption = ValueNone
        Core.create
        <| fun context ->
            let rec loop () =
                match inners with
                | ValueNone ->
                    match source.PollNext(context) with
                    | StreamPoll.Pending -> StreamPoll.Pending
                    | StreamPoll.Completed -> StreamPoll.Completed
                    | StreamPoll.Next x ->
                        let inners' = collector x
                        inners <- ValueSome inners'
                        loop ()
                | ValueSome inners' ->
                    let x = inners'.PollNext(context)
                    match x with
                    | StreamPoll.Pending -> StreamPoll.Pending
                    | StreamPoll.Completed ->
                        inners <- ValueNone
                        loop ()
                    | StreamPoll.Next x -> StreamPoll.Next x
            loop ()
        <| fun () ->
            source.Cancel()
            match inners with
            | ValueSome x ->
                x.Cancel()
                inners <- ValueNone
            | ValueNone -> ()

    /// Alias to `PullStream.collect`
    let inline bind binder source = collect binder source

    let iter (action: 'a -> unit) (source: IPullStream<'a>) : Future<unit> =
        let mutable source = source
        Future.Core.create
        <| fun context ->
            let rec loop () =
                match source.PollNext(context) with
                | StreamPoll.Completed -> Poll.Ready ()
                | StreamPoll.Pending -> Poll.Pending
                | StreamPoll.Next x ->
                    action x
                    loop ()
            loop ()
        <| fun () ->
            source.Cancel()
            source <- Unchecked.defaultof<_>

    let iterAsync (action: 'a -> Future<unit>) (source: IPullStream<'a>) : Future<unit> =
        let mutable currFut: Future<unit> voption = ValueNone
        Future.Core.create
        <| fun context ->
            let rec loop () =
                match currFut with
                | ValueNone ->
                    let x = source.PollNext(context)
                    match x with
                    | StreamPoll.Next x ->
                        let fut = action x
                        currFut <- ValueSome fut
                        loop ()
                    | StreamPoll.Pending -> Poll.Pending
                    | StreamPoll.Completed -> Poll.Ready ()
                | ValueSome fut ->
                    let futPoll = fut.Poll(context)
                    match futPoll with
                    | Poll.Ready () ->
                        currFut <- ValueNone
                        loop ()
                    | Poll.Pending -> Poll.Pending
            loop ()
        <| fun () ->
            source.Cancel()
            match currFut with
            | ValueSome fut ->
                fut.Cancel()
                currFut <- ValueNone
            | ValueNone -> ()

    let fold (folder: 's -> 'a -> 's) (initState: 's) (source: IPullStream<'a>): Future<'s> =
        let mutable currState = initState
        Future.Core.create
        <| fun context ->
            let rec loop () =
                let sPoll = source.PollNext(context)
                match sPoll with
                | StreamPoll.Pending -> Poll.Pending
                | StreamPoll.Completed -> Poll.Ready currState
                | StreamPoll.Next x ->
                    let state = folder currState x
                    currState <- state
                    loop ()
            loop ()
        <| fun () ->
            source.Cancel()

    let scan (folder: 's -> 'a -> 's) (initState: 's) (source: IPullStream<'a>) : IPullStream<'s> =
        let mutable currState = initState
        let mutable initReturned = false
        Core.create
        <| fun context ->
            if not initReturned then
                initReturned <- true
                StreamPoll.Next currState
            else
                let sPoll = source.PollNext(context)
                match sPoll with
                | StreamPoll.Pending -> StreamPoll.Pending
                | StreamPoll.Completed -> StreamPoll.Completed
                | StreamPoll.Next x ->
                    let state = folder currState x
                    currState <- state
                    StreamPoll.Next currState
        <| fun () ->
            source.Cancel()

    let chooseV (chooser: 'a -> 'b voption) (source: IPullStream<'a>) : IPullStream<'b> =
        Core.create
        <| fun context ->
            let rec loop () =
                let sPoll = source.PollNext(context)
                match sPoll with
                | StreamPoll.Pending -> StreamPoll.Pending
                | StreamPoll.Completed -> StreamPoll.Completed
                | StreamPoll.Next x ->
                    let r = chooser x
                    match r with
                    | ValueSome r -> StreamPoll.Next r
                    | ValueNone -> loop ()
            loop ()
        <| fun () ->
            source.Cancel()

    let tryPickV (chooser: 'a -> 'b voption) (source: IPullStream<'a>) : Future<'b voption> =
        let mutable result: 'b voption = ValueNone
        Future.Core.create
        <| fun context ->
            if result.IsSome then
                Poll.Ready result
            else
                let sPoll = source.PollNext(context)
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
        <| fun () ->
            source.Cancel()

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
        Core.create
        <| fun context ->
            if obj.ReferenceEquals(buffer, null) then
                StreamPoll.Completed
            else
            let rec loop () =
                let p = source.PollNext(context)
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
        <| fun () ->
            source.Cancel()
            buffer <- Unchecked.defaultof<_>

    let filter (predicate: 'a -> bool) (source: IPullStream<'a>) : IPullStream<'a> =
        Core.create
        <| fun context ->
            let rec loop () =
                let sPoll = source.PollNext(context)
                match sPoll with
                | StreamPoll.Pending -> StreamPoll.Pending
                | StreamPoll.Completed -> StreamPoll.Completed
                | StreamPoll.Next x ->
                    if predicate x then
                        StreamPoll.Next x
                    else
                        loop ()
            loop ()
        <| fun () ->
            source.Cancel()

    let any (predicate: 'a -> bool) (source: IPullStream<'a>) : Future<bool> =
        let mutable result: bool voption = ValueNone
        Future.Core.create
        <| fun context ->
            let rec loop () =
                match result with
                | ValueSome r ->
                    Poll.Ready r
                | ValueNone ->
                    let sPoll = source.PollNext(context)
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
        <| fun () ->
            source.Cancel()

    let all (predicate: 'a -> bool) (source: IPullStream<'a>) : Future<bool> =
        let mutable result: bool voption = ValueNone
        Future.Core.create
        <| fun context ->
            let rec loop () =
                match result with
                | ValueSome r -> Poll.Ready r
                | ValueNone ->
                    let sPoll = source.PollNext(context)
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
        <| fun () ->
            source.Cancel()

    let zip (source1: IPullStream<'a>) (source2: IPullStream<'b>) : IPullStream<'a * 'b> =

        let mutable v1 = ValueNone
        let mutable v2 = ValueNone

        Core.create
        <| fun ctx ->
            if v1.IsNone then
                v1 <- ValueSome (Core.pollNext ctx source1)
            if v2.IsNone then
                v2 <- ValueSome (Core.pollNext ctx source2)

            let inline getV x = match x with ValueSome x -> x | ValueNone -> invalidOp "unreachable"
            let r1, r2 = getV v1, getV v2
            match r1, r2 with
            | StreamPoll.Completed, _ ->
                source2.Cancel()
                StreamPoll.Completed
            | _, StreamPoll.Completed ->
                source1.Cancel()
                StreamPoll.Completed
            | StreamPoll.Pending, _ ->
                v1 <- ValueNone
                StreamPoll.Pending
            | _, StreamPoll.Pending ->
                v2 <- ValueNone
                StreamPoll.Pending
            | StreamPoll.Next x1, StreamPoll.Next x2 ->
                v1 <- ValueNone
                v2 <- ValueNone
                StreamPoll.Next (x1, x2)

        <| fun () ->
            source1.Cancel()
            source2.Cancel()

    let tryHeadV (source: IPullStream<'a>) : Future<'a voption> =
        Future.Core.memoizeReady
        <| fun context ->
            match source.PollNext(context) with
            | StreamPoll.Pending -> Poll.Pending
            | StreamPoll.Completed -> Poll.Ready ValueNone
            | StreamPoll.Next x -> Poll.Ready (ValueSome x)
        <| fun () ->
            source.Cancel()

    let tryLastV (source: IPullStream<'a>) : Future<'a voption> =
        Future.Core.memoizeReady
        <| fun context ->
            match source.PollNext(context) with
            | StreamPoll.Pending -> Poll.Pending
            | StreamPoll.Completed -> Poll.Ready ValueNone
            | StreamPoll.Next x -> Poll.Ready (ValueSome x)
        <| fun () ->
            source.Cancel()

    let ofFuture (fut: Future<'a>) : IPullStream<'a> =
        let mutable fut = fut // fut == null, when completed
        Core.create
        <| fun context ->
            if obj.ReferenceEquals(fut, null) then
                StreamPoll.Completed
            else
                let p = fut.Poll(context)
                match p with
                | Poll.Pending -> StreamPoll.Pending
                | Poll.Ready x ->
                    fut <- Unchecked.defaultof<_>
                    StreamPoll.Next x
        <| fun () ->
            if not (obj.ReferenceEquals(fut, null)) then
                fut.Cancel()

    let inline singleAsync x = ofFuture x

    let delay (u2S: unit -> IPullStream<'a>) : IPullStream<'a> =
        let mutable _inner: IPullStream<'a> voption = ValueNone
        Core.create
        <| fun context ->
            match _inner with
            | ValueNone ->
                let inner = u2S ()
                _inner <- ValueSome inner
                inner.PollNext(context)
            | ValueSome inner -> inner.PollNext(context)
        <| fun () ->
            match _inner with
            | ValueSome x ->
                x.Cancel()
                _inner <- ValueNone
            | ValueNone -> ()

    let take (count: int) (source: IPullStream<'a>) : IPullStream<'a> =
        let mutable _taken = 0
        Core.create
        <| fun context ->
            if _taken >= count then
                StreamPoll.Completed
            else
            let p = source.PollNext(context)
            match p with
            | StreamPoll.Pending -> StreamPoll.Pending
            | StreamPoll.Completed -> StreamPoll.Completed
            | StreamPoll.Next x ->
                _taken <- _taken + 1
                if _taken >= count then
                    source.Cancel()
                StreamPoll.Next x
        <| fun () ->
            source.Cancel()
