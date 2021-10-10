namespace FSharp.Control.Futures.Streams

open System.ComponentModel
open FSharp.Control.Futures.Core
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

// --------------------
// AsyncStreamer
// --------------------

/// # SeqStream pollNext schema
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x1 ] ->
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next x2 ] ->
/// ...
/// [ [ StreamPoll.Pending -> ...(may be inf)... -> StreamPoll.Pending ] -> StreamPoll.Next xn ] ->
/// [ StreamPoll.Pending -> ...(may be inf))... -> StreamPoll.Pending ] -> StreamPoll.Completed -> ... -> StreamPoll.Completed
///
/// x1 != x2 != ... != xn
[<Interface>]
type IAsyncStreamer<'a> =

    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract PollNext: IContext -> StreamPoll<'a>

    [<EditorBrowsable(EditorBrowsableState.Advanced)>]
    abstract Cancel: unit -> unit

exception StreamCancelledException
exception StreamCompletedException

[<RequireQualifiedAccess>]
module AsyncStreamer =

    let inline create (__expand_pollNext: IContext -> StreamPoll<'a>) (__expand_cancel: unit -> unit) =
        { new IAsyncStreamer<_> with
            member _.PollNext(ctx) = __expand_pollNext ctx
            member _.Cancel() = __expand_cancel () }

    let inline cancel (stream: IAsyncStreamer<'a>) =
        stream.Cancel()

    let inline cancelNullable (stream: IAsyncStreamer<'a>) =
        if not (obj.ReferenceEquals(stream, null)) then stream.Cancel()

    let inline pollNext (context: IContext) (stream: IAsyncStreamer<'a>) = stream.PollNext(context)

    // -----------
    // Creation
    // -----------

    let empty<'a> : IAsyncStreamer<'a> =
        create
        <| fun _ -> StreamPoll.Completed
        <| fun () -> do ()

    [<Struct>]
    type SingleState<'a> =
        | Value of 'a
        | Completed

    let single value =
        let mutable state = SingleState.Value value
        create
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
        create
        <| fun _ -> StreamPoll.Next value
        <| fun () -> do ()

    let never<'a> : IAsyncStreamer<'a> =
        create
        <| fun _ -> StreamPoll.Pending
        <| fun () -> do ()

    let replicate count value =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        create
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
        create
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
        create
        <| fun _ ->
            let x = initializer current
            current <- current + 1
            StreamPoll.Next x
        <| fun () ->
            do ()

    let ofSeq (src: 'a seq) : IAsyncStreamer<'a> =
        let mutable _enumerator = src.GetEnumerator()
        create
        <| fun _ ->
            if _enumerator.MoveNext()
            then StreamPoll.Next _enumerator.Current
            else StreamPoll.Completed
        <| fun () -> _enumerator <- Unchecked.defaultof<_>


    // -----------
    // Combinators
    // -----------

    let map (mapper: 'a -> 'b) (source: IAsyncStreamer<'a>) : IAsyncStreamer<'b> =
        create
        <| fun context -> source.PollNext(context) |> StreamPoll.map mapper
        <| fun () -> do source.Cancel()

    let collect (collector: 'a -> IAsyncStreamer<'b>) (source: IAsyncStreamer<'a>) : IAsyncStreamer<'b> =

        // Берем по одному IStream<'b> из _source, перебираем их элементы, пока каждый из них не закончится

        let mutable _source = source
        let mutable _inners: IAsyncStreamer<'b> = Unchecked.defaultof<_>
        create
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

    let iter (action: 'a -> unit) (source: IAsyncStreamer<'a>) : IFuture<unit> =
        let mutable _source = source
        Future.create
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

    let iterAsync (action: 'a -> IFuture<unit>) (source: IAsyncStreamer<'a>) : IFuture<unit> =
        let mutable _currFut: IFuture<unit> voption = ValueNone
        Future.create
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

    let fold (folder: 's -> 'a -> 's) (initState: 's) (source: IAsyncStreamer<'a>): IFuture<'s> =
        let mutable _currState = initState
        Future.create
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

    let scan (folder: 's -> 'a -> 's) (initState: 's) (source: IAsyncStreamer<'a>) : IAsyncStreamer<'s> =
        let mutable _currState = initState
        let mutable _initReturned = false
        create
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

    let chooseV (chooser: 'a -> 'b voption) (source: IAsyncStreamer<'a>) : IAsyncStreamer<'b> =
        create
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

    let tryPickV (chooser: 'a -> 'b voption) (source: IAsyncStreamer<'a>) : IFuture<'b voption> =
        let mutable _source = source
        let mutable _result: 'b voption = ValueNone
        Future.create
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

    let tryPick (chooser: 'a -> 'b option) (source: IAsyncStreamer<'a>) : IFuture<'b option> =
        tryPickV (chooser >> Option.toValueOption) source |> Future.map Option.ofValueOption

    let pickV (chooser: 'a -> 'b voption) (source: IAsyncStreamer<'a>) : IFuture<'b> =
        tryPickV chooser source
        |> Future.map ^function
            | ValueSome r -> r
            | ValueNone -> raise (System.Collections.Generic.KeyNotFoundException())

    let join (source: IAsyncStreamer<IAsyncStreamer<'a>>) : IAsyncStreamer<'a> =
        bind id source

    let append (source1: IAsyncStreamer<'a>) (source2: IAsyncStreamer<'a>) : IAsyncStreamer<'a> =
        let mutable _source1 = source1 // when = null -- already completed
        let mutable _source2 = source2 // when _source1 and _source2 = null then completed

        create
        <| fun ctx ->
            if isNotNull _source1 then
                _source1
                |> pollNext ctx
                |> StreamPoll.bindCompleted (fun () ->
                    _source1 <- Unchecked.defaultof<_>
                    _source2
                    |> pollNext ctx
                    |> StreamPoll.mapCompleted (fun () -> _source2 <- Unchecked.defaultof<_>)
                )
            elif isNotNull _source2 then
                _source2
                |> pollNext ctx
                |> StreamPoll.mapCompleted (fun () -> _source2 <- Unchecked.defaultof<_>)
            else StreamPoll.Completed
        <| fun () ->
            cancelNullable _source1
            cancelNullable _source2

    let bufferByCount (bufferSize: int) (source: IAsyncStreamer<'a>) : IAsyncStreamer<'a[]> =
        let mutable buffer = Array.zeroCreate bufferSize
        let mutable currIdx = 0
        create
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

    let filter (predicate: 'a -> bool) (source: IAsyncStreamer<'a>) : IAsyncStreamer<'a> =
        create
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

    let any (predicate: 'a -> bool) (source: IAsyncStreamer<'a>) : IFuture<bool> =
        let mutable result: bool voption = ValueNone
        Future.create
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
        <| source.Cancel

    let all (predicate: 'a -> bool) (source: IAsyncStreamer<'a>) : IFuture<bool> =
        let mutable result: bool voption = ValueNone
        Future.create
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

    let zip (source1: IAsyncStreamer<'a>) (source2: IAsyncStreamer<'b>) : IAsyncStreamer<'a * 'b> =

        let mutable v1 = ValueNone
        let mutable v2 = ValueNone

        create
        <| fun ctx ->
            if v1.IsNone then
                v1 <- ValueSome (pollNext ctx source1)
            if v2.IsNone then
                v2 <- ValueSome (pollNext ctx source2)

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

    let tryHeadV (source: IAsyncStreamer<'a>) : IFuture<'a voption> =
        Future.createMemo
        <| fun context ->
            match source.PollNext(context) with
            | StreamPoll.Pending -> Poll.Pending
            | StreamPoll.Completed -> Poll.Ready ValueNone
            | StreamPoll.Next x -> Poll.Ready (ValueSome x)
        <| source.Cancel

    let tryHead (source: IAsyncStreamer<'a>) : IFuture<'a option> =
        tryHeadV source |> Future.map (function ValueSome x -> Some x | ValueNone -> None)

    let head (source: IAsyncStreamer<'a>) : IFuture<'a> =
        tryHeadV source
        |> Future.map (function
            | ValueSome x -> x
            | ValueNone -> invalidArg (nameof source) "The input stream was empty."
        )

    let tryLastV (source: IAsyncStreamer<'a>) : IFuture<'a voption> =
        let mutable last = ValueNone
        Future.createMemo
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

    let tryLast (source: IAsyncStreamer<'a>) : IFuture<'a option> =
        tryLastV source |> Future.map (function ValueSome x -> Some x | ValueNone -> None)

    let last (source: IAsyncStreamer<'a>) : IFuture<'a> =
        tryLastV source
        |> Future.map (function
            | ValueSome x -> x
            | ValueNone -> invalidArg (nameof source) "The input stream was empty."
        )

    let ofComputation (fut: IFuture<'a>) : IAsyncStreamer<'a> =
        let mutable _fut = fut // fut == null, when completed
        create
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
            Future.cancelIfNotNull _fut

    let inline singleAsync x = ofComputation x

    let delay (u2S: unit -> IAsyncStreamer<'a>) : IAsyncStreamer<'a> =
        let mutable _inner: IAsyncStreamer<'a> voption = ValueNone
        create
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

    let take (count: int) (source: IAsyncStreamer<'a>) : IAsyncStreamer<'a> =
        let mutable _taken = 0
        create
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



// --------------------
// Stream
// --------------------

[<Interface>]
type IStream<'a> =
    abstract RunStreaming: unit -> IAsyncStreamer<'a>

type Stream<'a> = IStream<'a>

module Stream =

    let inline create (__expand_f: unit -> IAsyncStreamer<'a>) =
        { new IStream<'a> with member _.RunStreaming() = __expand_f () }

    let inline runStreaming (s: Stream<'a>) =
        s.RunStreaming()

    // ---------
    // Creation
    // ---------

    let inline empty<'a> : Stream<'a> =
        create (fun () -> AsyncStreamer.empty)

    let inline single value =
        create (fun () -> AsyncStreamer.single value)

    /// Always returns SeqNext of the value
    let inline always value =
        create (fun () -> AsyncStreamer.always value)

    let inline never<'a> : Stream<'a> =
        create (fun () -> AsyncStreamer.never<'a>)

    let inline replicate count value =
        create (fun () -> AsyncStreamer.replicate count value)

    let inline init count initializer =
        create (fun () -> AsyncStreamer.init count initializer)

    let inline initInfinite initializer =
        create (fun () -> AsyncStreamer.initInfinite initializer)

    let inline ofSeq (src: 'a seq) : Stream<'a> =
        create (fun () -> AsyncStreamer.ofSeq src)

    // -----------
    // Combinators
    // -----------

    let inline map (mapper: 'a -> 'b) (source: Stream<'a>) : Stream<'b> =
        create (fun () -> AsyncStreamer.map mapper (runStreaming source))

    let inline collect (collector: 'a -> Stream<'b>) (source: Stream<'a>) : Stream<'b> =
        create (fun () -> AsyncStreamer.collect (collector >> runStreaming) (runStreaming source))

    /// Alias to `Stream.collect`
    let inline bind binder source = collect binder source

    let inline iter (action: 'a -> unit) (source: Stream<'a>) : Future<unit> =
        Future_OLD.create (fun () -> AsyncStreamer.iter action (runStreaming source))

    let inline iterAsync (action: 'a -> Future<unit>) (source: Stream<'a>) : Future<unit> =
        Future_OLD.create (fun () -> AsyncStreamer.iterAsync (action >> Future.startComputation) (runStreaming source))

    let inline fold (folder: 's -> 'a -> 's) (initState: 's) (source: Stream<'a>): Future<'s> =
        Future_OLD.create (fun () -> AsyncStreamer.fold folder initState (runStreaming source))

    let inline scan (folder: 's -> 'a -> 's) (initState: 's) (source: Stream<'a>) : Stream<'s> =
        create (fun () -> AsyncStreamer.scan folder initState (runStreaming source))

    let inline chooseV (chooser: 'a -> 'b voption) (source: Stream<'a>) : Stream<'b> =
        create (fun () -> AsyncStreamer.chooseV chooser (runStreaming source))

    let inline tryPickV (chooser: 'a -> 'b voption) (source: Stream<'a>) : Future<'b voption> =
        Future_OLD.create (fun () -> AsyncStreamer.tryPickV chooser (runStreaming source))

    let inline tryPick (chooser: 'a -> 'b option) (source: Stream<'a>) : Future<'b option> =
        Future_OLD.create (fun () -> AsyncStreamer.tryPick chooser (runStreaming source))

    let inline pickV (chooser: 'a -> 'b voption) (source: Stream<'a>) : Future<'b> =
        Future_OLD.create (fun () -> AsyncStreamer.pickV chooser (runStreaming source))

    let inline join (source: Stream<Stream<'a>>) : Stream<'a> =
        create (fun () -> AsyncStreamer.join (runStreaming (map runStreaming source)))

    let inline append (source1: Stream<'a>) (source2: Stream<'a>) : Stream<'a> =
        create (fun () -> AsyncStreamer.append (runStreaming source1) (runStreaming source2))

    let inline bufferByCount (bufferSize: int) (source: Stream<'a>) : Stream<'a[]> =
        create (fun () -> AsyncStreamer.bufferByCount bufferSize (runStreaming source))

    let inline filter (predicate: 'a -> bool) (source: Stream<'a>) : Stream<'a> =
        create (fun () -> AsyncStreamer.filter predicate (runStreaming source))

    let inline any (predicate: 'a -> bool) (source: Stream<'a>) : Future<bool> =
        Future_OLD.create (fun () -> AsyncStreamer.any predicate (runStreaming source))

    let inline all (predicate: 'a -> bool) (source: Stream<'a>) : Future<bool> =
        Future_OLD.create (fun () -> AsyncStreamer.all predicate (runStreaming source))

    let inline zip (source1: Stream<'a>) (source2: Stream<'b>) : Stream<'a * 'b> =
        create (fun () -> AsyncStreamer.zip (runStreaming source1) (runStreaming source2))

    let inline tryHeadV (source: Stream<'a>) : Future<'a voption> =
        Future_OLD.create (fun () -> AsyncStreamer.tryHeadV (runStreaming source))

    let inline tryHead (source: Stream<'a>) : Future<'a option> =
        Future_OLD.create (fun () -> AsyncStreamer.tryHead (runStreaming source))

    let inline head (source: Stream<'a>) : Future<'a> =
        Future_OLD.create (fun () -> AsyncStreamer.head (runStreaming source))

    let inline tryLastV (source: Stream<'a>) : Future<'a voption> =
        Future_OLD.create (fun () -> AsyncStreamer.tryLastV (runStreaming source))

    let inline tryLast (source: Stream<'a>) : Future<'a option> =
        Future_OLD.create (fun () -> AsyncStreamer.tryLast (runStreaming source))

    let inline last (source: Stream<'a>) : Future<'a> =
        Future_OLD.create (fun () -> AsyncStreamer.last (runStreaming source))

    let inline singleAsync (x: Future<'a>) : Stream<'a> =
        create (fun () -> AsyncStreamer.ofComputation (Future_OLD.startComputation x))

    let inline take (count: int) (source: Stream<'a>) : Stream<'a> =
        create (fun () -> AsyncStreamer.take count (runStreaming source))
