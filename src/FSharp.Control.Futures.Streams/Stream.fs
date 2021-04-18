namespace FSharp.Control.Futures.Streams

open System.ComponentModel
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

    let inline mapCompleted action poll =
        match poll with
        | StreamPoll.Completed -> action (); poll
        | _ -> poll

    let inline bindCompleted binder poll =
        match poll with
        | StreamPoll.Completed -> binder ()
        | _ -> poll



/// # SeqStream pollNext schema
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x1 ] ->
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x2 ] ->
/// ...
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next xn ] ->
/// [ StreamPoll.Pending -> ...(may be inf))... -> StreamPoll.Pending ] -> StreamPoll.Completed -> ... -> StreamPoll.Completed
///
/// x1 != x2 != ... != xn
[<Interface>]
type IStream<'a> =

    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract PollNext: Context -> StreamPoll<'a>

    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel: unit -> unit

exception StreamCancelledException
exception StreamCompletedException

[<RequireQualifiedAccess>]
module Stream =

    [<RequireQualifiedAccess>]
    module Core =

        let inline create (__expand_pollNext: Context -> StreamPoll<'a>) (__expand_cancel: unit -> unit) =
            { new IStream<_> with
                member _.PollNext(ctx) = __expand_pollNext ctx
                member _.Cancel() = __expand_cancel () }

        let inline cancel (stream: IStream<'a>) =
            stream.Cancel()

        let inline cancelNullable (stream: IStream<'a>) =
            if not (obj.ReferenceEquals(stream, null)) then stream.Cancel()

        let inline pollNext (context: Context) (stream: IStream<'a>) = stream.PollNext(context)

    // -----------
    // Creation
    // -----------

    let empty<'a> : IStream<'a> =
        Core.create
        <| fun _ -> StreamPoll.Completed
        <| fun () -> do ()

    [<Struct>]
    type SingleState<'a> =
        | Value of 'a
        | Completed

    let single value =
        let mutable state = SingleState.Value value
        Core.create
        <| fun _ ->
            match state with
            | Value x ->
                state <- Completed
                StreamPoll.Next x
            | Completed -> StreamPoll.Completed
        <| fun () ->
            match state with
            | Value _ -> state <- Completed
            | Completed -> state <- Completed

    /// Always returns SeqNext of the value
    let always value =
        Core.create
        <| fun _ -> StreamPoll.Next value
        <| fun () -> do ()

    let never<'a> : IStream<'a> =
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

    let ofSeq (src: 'a seq) : IStream<'a> =
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

    let map (mapper: 'a -> 'b) (source: IStream<'a>) : IStream<'b> =
        Core.create
        <| fun context -> source.PollNext(context) |> StreamPoll.map mapper
        <| fun () -> do source.Cancel()

    let collect (collector: 'a -> IStream<'b>) (source: IStream<'a>) : IStream<'b> =

        // Берем по одному IStream<'b> из _source, перебираем их элементы, пока каждый из них не закончится

        let mutable _source = source
        let mutable _inners: IStream<'b> = Unchecked.defaultof<_>
        Core.create
        <| fun context ->
            let mutable _result = ValueNone
            while _result.IsNone do
                if obj.ReferenceEquals(_inners, null)
                then
                    match _source.PollNext(context) with
                    | StreamPoll.Pending -> _result <- ValueSome StreamPoll.Pending
                    | StreamPoll.Completed -> _result <- ValueSome StreamPoll.Completed
                    | StreamPoll.Next x -> _inners <- collector x
                else
                    let x = _inners.PollNext(context)
                    match x with
                    | StreamPoll.Pending -> _result <- ValueSome StreamPoll.Pending
                    | StreamPoll.Completed -> _inners <- Unchecked.defaultof<_>
                    | StreamPoll.Next x -> _result <- ValueSome (StreamPoll.Next x)

            ValueOption.get _result

        <| fun () ->
            _source.Cancel()
            if not (obj.ReferenceEquals(_inners, null)) then
                _inners.Cancel()
                _inners <- Unchecked.defaultof<_>

    /// Alias to `PullStream.collect`
    let inline bind binder source = collect binder source

    let iter (action: 'a -> unit) (source: IStream<'a>) : Future<unit> =
        let mutable _source = source
        Future.Core.create
        <| fun context ->
            let mutable _result = ValueNone
            while _result.IsNone do
                match _source.PollNext(context) with
                | StreamPoll.Completed -> _result <- ValueSome (Poll.Ready ())
                | StreamPoll.Pending -> _result <- ValueSome Poll.Pending
                | StreamPoll.Next x -> action x
            ValueOption.get _result
        <| fun () ->
            _source.Cancel()
            _source <- Unchecked.defaultof<_>

    let iterAsync (action: 'a -> Future<unit>) (source: IStream<'a>) : Future<unit> =
        let mutable _currFut: Future<unit> voption = ValueNone
        Future.Core.create
        <| fun context ->
            let rec loop () =
                match _currFut with
                | ValueNone ->
                    let x = source.PollNext(context)
                    match x with
                    | StreamPoll.Next x ->
                        let fut = action x
                        _currFut <- ValueSome fut
                        loop ()
                    | StreamPoll.Pending -> Poll.Pending
                    | StreamPoll.Completed -> Poll.Ready ()
                | ValueSome fut ->
                    let futPoll = fut.Poll(context)
                    match futPoll with
                    | Poll.Ready () ->
                        _currFut <- ValueNone
                        loop ()
                    | Poll.Pending -> Poll.Pending
            loop ()
        <| fun () ->
            source.Cancel()
            match _currFut with
            | ValueSome fut ->
                fut.Cancel()
                _currFut <- ValueNone
            | ValueNone -> ()

    let fold (folder: 's -> 'a -> 's) (initState: 's) (source: IStream<'a>): Future<'s> =
        let mutable _currState = initState
        Future.Core.create
        <| fun context ->
            let rec loop () =
                let sPoll = source.PollNext(context)
                match sPoll with
                | StreamPoll.Pending -> Poll.Pending
                | StreamPoll.Completed -> Poll.Ready _currState
                | StreamPoll.Next x ->
                    let state = folder _currState x
                    _currState <- state
                    loop ()
            loop ()
        <| fun () ->
            source.Cancel()

    let scan (folder: 's -> 'a -> 's) (initState: 's) (source: IStream<'a>) : IStream<'s> =
        let mutable _currState = initState
        let mutable _initReturned = false
        Core.create
        <| fun context ->
            if not _initReturned then
                _initReturned <- true
                StreamPoll.Next _currState
            else
                let sPoll = source.PollNext(context)
                match sPoll with
                | StreamPoll.Pending -> StreamPoll.Pending
                | StreamPoll.Completed -> StreamPoll.Completed
                | StreamPoll.Next x ->
                    let state = folder _currState x
                    _currState <- state
                    StreamPoll.Next _currState
        <| fun () ->
            source.Cancel()

    let chooseV (chooser: 'a -> 'b voption) (source: IStream<'a>) : IStream<'b> =
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

    let tryPickV (chooser: 'a -> 'b voption) (source: IStream<'a>) : Future<'b voption> =
        let mutable _source = source
        let mutable _result: 'b voption = ValueNone
        Future.Core.create
        <| fun context ->
            if _result.IsSome then
                Poll.Ready _result
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
                        _result <- ValueSome r
                        _source <- Unchecked.defaultof<_>
                        Poll.Ready _result
        <| fun () ->
            source.Cancel()

    let pickV (chooser: 'a -> 'b voption) (source: IStream<'a>) : Future<'b> =
        tryPickV chooser source
        |> Future.map ^function
            | ValueSome r -> r
            | ValueNone -> raise (System.Collections.Generic.KeyNotFoundException())

    let join (source: IStream<IStream<'a>>) : IStream<'a> =
        bind id source

    let append (source1: IStream<'a>) (source2: IStream<'a>) : IStream<'a> =
        let mutable _source1 = source1 // when = null -- already completed
        let mutable _source2 = source2 // when _source1 and _source2 = null then completed

        Core.create
        <| fun ctx ->
            if isNotNull _source1 then
                _source1
                |> Core.pollNext ctx
                |> StreamPoll.bindCompleted (fun () ->
                    _source1 <- Unchecked.defaultof<_>
                    _source2
                    |> Core.pollNext ctx
                    |> StreamPoll.mapCompleted (fun () -> _source2 <- Unchecked.defaultof<_>)
                )
            elif isNotNull _source2 then
                _source2
                |> Core.pollNext ctx
                |> StreamPoll.mapCompleted (fun () -> _source2 <- Unchecked.defaultof<_>)
            else StreamPoll.Completed
        <| fun () ->
            Core.cancelNullable _source1
            Core.cancelNullable _source2

    let bufferByCount (bufferSize: int) (source: IStream<'a>) : IStream<'a[]> =
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
                        let buffer' = buffer
                        buffer <- Array.zeroCreate bufferSize
                        StreamPoll.Next buffer'
                    else
                        buffer.[currIdx] <- x
                        currIdx <- currIdx + 1
                        loop ()
            loop ()
        <| fun () ->
            source.Cancel()
            buffer <- Unchecked.defaultof<_>

    let filter (predicate: 'a -> bool) (source: IStream<'a>) : IStream<'a> =
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

    let any (predicate: 'a -> bool) (source: IStream<'a>) : Future<bool> =
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

    let all (predicate: 'a -> bool) (source: IStream<'a>) : Future<bool> =
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

    let zip (source1: IStream<'a>) (source2: IStream<'b>) : IStream<'a * 'b> =

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

    let tryHeadV (source: IStream<'a>) : Future<'a voption> =
        Future.Core.createMemo
        <| fun context ->
            match source.PollNext(context) with
            | StreamPoll.Pending -> Poll.Pending
            | StreamPoll.Completed -> Poll.Ready ValueNone
            | StreamPoll.Next x -> Poll.Ready (ValueSome x)
        <| fun () ->
            source.Cancel()

    let head (source: IStream<'a>) : Future<'a> =
        future {
            let! head = tryHeadV source
            match head with
            | ValueSome x -> return x
            | ValueNone -> return raise StreamCompletedException
        }

    let tryLastV (source: IStream<'a>) : Future<'a voption> =
        let mutable last = ValueNone
        Future.Core.createMemo
        <| fun context ->
            let rec loop () =
                match source.PollNext(context) with
                | StreamPoll.Pending -> Poll.Pending
                | StreamPoll.Completed -> Poll.Ready last
                | StreamPoll.Next x ->
                    last <- ValueSome x
                    loop ()
            loop ()
        <| fun () ->
            source.Cancel()

    let last (source: IStream<'a>) : Future<'a> =
        future {
            let! head = tryLastV source
            match head with
            | ValueSome x -> return x
            | ValueNone -> return raise StreamCompletedException
        }

    let ofFuture (fut: Future<'a>) : IStream<'a> =
        let mutable _fut = fut // fut == null, when completed
        Core.create
        <| fun context ->
            if obj.ReferenceEquals(_fut, null) then
                StreamPoll.Completed
            else
                let p = _fut.Poll(context)
                match p with
                | Poll.Pending -> StreamPoll.Pending
                | Poll.Ready x ->
                    _fut <- Unchecked.defaultof<_>
                    StreamPoll.Next x
        <| fun () ->
            Future.Core.cancelNullable _fut

    let inline singleAsync x = ofFuture x

    let delay (u2S: unit -> IStream<'a>) : IStream<'a> =
        let mutable _inner: IStream<'a> voption = ValueNone
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

    let take (count: int) (source: IStream<'a>) : IStream<'a> =
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
