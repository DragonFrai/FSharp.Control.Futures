namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures
open FSharp.Control.Futures.Core
open FSharp.Control.Futures.Streams.Core


exception StreamCancelledException
exception StreamCompletedException

type Stream<'a> = Core.Stream<'a>

[<RequireQualifiedAccess>]
module Stream =

    let inline cancelNullable (stream: Stream<'a>) =
        if not (obj.ReferenceEquals(stream, null)) then stream.Close()

    // -----------
    // Creation
    // -----------

    let empty<'a> : Stream<'a> =
        Stream.create
        <| fun _ -> StreamPoll.Completed
        <| fun () -> do ()

    [<Struct>]
    type SingleState<'a> =
        | Value of 'a
        | Completed

    let single value =
        let mutable state = SingleState.Value value
        Stream.create
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
        Stream.create
        <| fun _ -> StreamPoll.Next value
        <| fun () -> do ()

    let never<'a> : Stream<'a> =
        Stream.create
        <| fun _ -> StreamPoll.Pending
        <| fun () -> do ()

    let replicate count value =
        if count < 0 then invalidArg (nameof count) "count < 0"
        let mutable current = 0
        Stream.create
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
        Stream.create
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
        Stream.create
        <| fun _ ->
            let x = initializer current
            current <- current + 1
            StreamPoll.Next x
        <| fun () ->
            do ()

    let ofSeq (src: 'a seq) : Stream<'a> =
        let mutable _enumerator = src.GetEnumerator()
        Stream.create
        <| fun _ ->
            if _enumerator.MoveNext()
            then StreamPoll.Next _enumerator.Current
            else StreamPoll.Completed
        <| fun () -> _enumerator <- Unchecked.defaultof<_>


    // -----------
    // Combinators
    // -----------

    let map (mapper: 'a -> 'b) (source: Stream<'a>) : Stream<'b> =
        Stream.create
        <| fun context -> source.PollNext(context) |> StreamPoll.map mapper
        <| fun () -> do source.Close()

    let collect (collector: 'a -> Stream<'b>) (source: Stream<'a>) : Stream<'b> =

        // Берем по одному Stream<'b> из _source, перебираем их элементы, пока каждый из них не закончится

        let mutable _source = source
        let mutable _inners: Stream<'b> = Unchecked.defaultof<_>
        Stream.create
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
            _source.Close()
            if not (obj.ReferenceEquals(_inners, null)) then
                _inners.Close()
                _inners <- Unchecked.defaultof<_>

    /// Alias to `PullStream.collect`
    let inline bind binder source = collect binder source

    let iter (action: 'a -> unit) (source: Stream<'a>) : IFuture<unit> =
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
            _source.Close()
            _source <- Unchecked.defaultof<_>

    let iterAsync (action: 'a -> IFuture<unit>) (source: Stream<'a>) : IFuture<unit> =
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
            source.Close()
            match _currFut with
            | ValueSome fut ->
                fut.Cancel()
                _currFut <- ValueNone
            | ValueNone -> ()

    let fold (folder: 's -> 'a -> 's) (initState: 's) (source: Stream<'a>): IFuture<'s> =
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
            source.Close()

    let scan (folder: 's -> 'a -> 's) (initState: 's) (source: Stream<'a>) : Stream<'s> =
        let mutable _currState = initState
        let mutable _initReturned = false
        Stream.create
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
            source.Close()

    let chooseV (chooser: 'a -> 'b voption) (source: Stream<'a>) : Stream<'b> =
        Stream.create
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
            source.Close()

    let tryPickV (chooser: 'a -> 'b voption) (source: Stream<'a>) : IFuture<'b voption> =
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
            source.Close()

    let tryPick (chooser: 'a -> 'b option) (source: Stream<'a>) : IFuture<'b option> =
        tryPickV (chooser >> Option.toValueOption) source |> Future.map Option.ofValueOption

    let pickV (chooser: 'a -> 'b voption) (source: Stream<'a>) : IFuture<'b> =
        tryPickV chooser source
        |> Future.map ^function
            | ValueSome r -> r
            | ValueNone -> raise (System.Collections.Generic.KeyNotFoundException())

    let join (source: Stream<Stream<'a>>) : Stream<'a> =
        bind id source

    let append (source1: Stream<'a>) (source2: Stream<'a>) : Stream<'a> =
        let mutable _source1 = source1 // when = null -- already completed
        let mutable _source2 = source2 // when _source1 and _source2 = null then completed

        Stream.create
        <| fun ctx ->
            if isNotNull _source1 then
                _source1
                |> Stream.pollNext ctx
                |> StreamPoll.bindCompleted (fun () ->
                    _source1 <- Unchecked.defaultof<_>
                    _source2
                    |> Stream.pollNext ctx
                    |> StreamPoll.mapCompleted (fun () -> _source2 <- Unchecked.defaultof<_>)
                )
            elif isNotNull _source2 then
                _source2
                |> Stream.pollNext ctx
                |> StreamPoll.mapCompleted (fun () -> _source2 <- Unchecked.defaultof<_>)
            else StreamPoll.Completed
        <| fun () ->
            cancelNullable _source1
            cancelNullable _source2

    let bufferByCount (bufferSize: int) (source: Stream<'a>) : Stream<'a[]> =
        let mutable buffer = Array.zeroCreate bufferSize
        let mutable currIdx = 0
        Stream.create
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
            source.Close()
            buffer <- Unchecked.defaultof<_>

    let filter (predicate: 'a -> bool) (source: Stream<'a>) : Stream<'a> =
        Stream.create
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
            source.Close()

    let any (predicate: 'a -> bool) (source: Stream<'a>) : IFuture<bool> =
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
        <| source.Close

    let all (predicate: 'a -> bool) (source: Stream<'a>) : IFuture<bool> =
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
            source.Close()

    let zip (source1: Stream<'a>) (source2: Stream<'b>) : Stream<'a * 'b> =

        let mutable v1 = ValueNone
        let mutable v2 = ValueNone

        Stream.create
        <| fun ctx ->
            if v1.IsNone then
                v1 <- ValueSome (Stream.pollNext ctx source1)
            if v2.IsNone then
                v2 <- ValueSome (Stream.pollNext ctx source2)

            let inline getV x = match x with ValueSome x -> x | ValueNone -> invalidOp "unreachable"
            let r1, r2 = getV v1, getV v2
            match r1, r2 with
            | StreamPoll.Completed, _ ->
                source2.Close()
                StreamPoll.Completed
            | _, StreamPoll.Completed ->
                source1.Close()
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
            source1.Close()
            source2.Close()

    let tryHeadV (source: Stream<'a>) : IFuture<'a voption> =
        Future.create
        <| fun context ->
            match source.PollNext(context) with
            | StreamPoll.Pending -> Poll.Pending
            | StreamPoll.Completed -> Poll.Ready ValueNone
            | StreamPoll.Next x -> Poll.Ready (ValueSome x)
        <| source.Close

    let tryHead (source: Stream<'a>) : IFuture<'a option> =
        tryHeadV source |> Future.map (function ValueSome x -> Some x | ValueNone -> None)

    let head (source: Stream<'a>) : IFuture<'a> =
        tryHeadV source
        |> Future.map (function
            | ValueSome x -> x
            | ValueNone -> invalidArg (nameof source) "The input stream was empty."
        )

    let tryLastV (source: Stream<'a>) : IFuture<'a voption> =
        let mutable last = ValueNone
        Future.create
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
            source.Close()

    let tryLast (source: Stream<'a>) : IFuture<'a option> =
        tryLastV source |> Future.map (function ValueSome x -> Some x | ValueNone -> None)

    let last (source: Stream<'a>) : IFuture<'a> =
        tryLastV source
        |> Future.map (function
            | ValueSome x -> x
            | ValueNone -> invalidArg (nameof source) "The input stream was empty."
        )

    let ofComputation (fut: IFuture<'a>) : Stream<'a> =
        let mutable _fut = fut // fut == null, when completed
        Stream.create
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
            Internals.Helpers.cancelIfNotNull _fut

    let inline singleAsync x = ofComputation x

    let delay (u2S: unit -> Stream<'a>) : Stream<'a> =
        let mutable _inner: Stream<'a> voption = ValueNone
        Stream.create
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
                x.Close()
                _inner <- ValueNone
            | ValueNone -> ()

    let take (count: int) (source: Stream<'a>) : Stream<'a> =
        let mutable _taken = 0
        Stream.create
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
                    source.Close()
                StreamPoll.Next x
        <| fun () ->
            source.Close()
