namespace FSharp.Control.Futures.Streams

open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel
open FSharp.Control.Futures.Streams
open FSharp.Control.Futures.Streams.LowLevel


[<RequireQualifiedAccess>]
module Streams =

    [<Sealed>]
    type Empty<'a> private () =
        static member Instance: Empty<'a> = Empty<'a>()
        interface IStream<'a> with
            member this.PollNext(_ctx) = PollNext.Completed
            member this.Drop() = do ()

    [<Sealed>]
    type Always<'a>(value: 'a) =
        interface IStream<'a> with
            member this.PollNext(_ctx) = PollNext.Next value
            member this.Drop() = do ()

    [<Sealed>]
    type Never<'a> private () =
        static member Instance = Never<'a>()
        interface IStream<'a> with
            member this.PollNext(_ctx) = PollNext.Pending
            member this.Drop() = do ()

    [<Sealed>]
    type Seq<'a>(source: 'a seq) =
        let enumerator = source.GetEnumerator()
        interface IStream<'a> with
            member this.PollNext(_ctx) =
                if enumerator.MoveNext()
                then PollNext.Next enumerator.Current
                else PollNext.Completed

    [<Sealed>]
    type Single<'a>(source: IFuture<'a>) =
        let mutable source = NaiveFuture(source)
        interface IStream<'a> with
            override this.PollNext(context) =
                if source.IsNotNull then
                    match source.Poll(context) with
                    | NaivePoll.Pending -> PollNext.Pending
                    | NaivePoll.Ready value ->
                        source <- NaiveFuture.Null
                        PollNext.Next value
                else
                    PollNext.Completed

            override this.Drop() =
                source.Drop()

    [<Sealed>]
    type SingleValue<'a>(value: 'a) =
        let mutable source = ValueSome value
        interface IStream<'a> with
            override this.PollNext(context) =
                match source with
                | ValueSome value ->
                    source <- ValueNone
                    PollNext.Next value
                | ValueNone ->
                    PollNext.Completed

            override this.Drop() =
                ()

    [<Sealed>]
    type Map<'a, 'b>(mapping: 'a -> 'b, source: IStream<'a>) =
        let mutable source = NaiveStream(source)
        interface IStream<'b> with
            override this.PollNext(context) =
                match source.PollNext(context) with
                | NaivePollNext.Pending -> PollNext.Pending
                | NaivePollNext.Completed -> PollNext.Completed
                | NaivePollNext.Next value -> PollNext.Next (mapping value)
            override this.Drop() = source.Drop()

    [<Sealed>]
    type Bind<'a, 'b>(binder: 'a -> IStream<'b>, source: IStream<'a>) =
        let mutable source = NaiveStream(source)
        let mutable binded = NaiveStream<'b>.Null()

        interface IStream<'b> with
            override this.PollNext(context) =
                let mutable doLoop = true
                let mutable result = Unchecked.defaultof<_>
                while doLoop do
                    if not binded.IsNull then
                        // 'yieldB
                        let pollNextB = binded.PollNext(context)
                        match pollNextB with
                        | NaivePollNext.Pending ->
                            doLoop <- false
                            result <- PollNext.Pending
                        | NaivePollNext.Completed ->
                            binded.MakeNull()
                            // MakeEmpty to jump to other if branch ("'waitA")
                            ()
                        | NaivePollNext.Next value ->
                            doLoop <- false
                            result <- PollNext.Next value
                    else
                        // 'waitA
                        let pollNextA = source.PollNext(context)
                        match pollNextA with
                        | NaivePollNext.Pending ->
                            doLoop <- false
                            result <- PollNext.Pending
                        | NaivePollNext.Completed ->
                            doLoop <- false
                            result <- PollNext.Completed
                        | NaivePollNext.Next value ->
                            let binded' = binder value
                            binded <- NaiveStream(binded')
                result

            override this.Drop() =
                if not binded.IsNull then
                    binded.Drop()
                source.Drop()


module Futures =

    [<Sealed>]
    type IterAsync<'a> =

        val private Action: 'a -> IFuture<unit>
        val private Source: NaiveStream<'a>
        val mutable ActionFuture: NaiveFuture<unit>

        new(action: 'a -> IFuture<unit>, source: IStream<'a>) =
            { Action = action
              Source = NaiveStream(source)
              ActionFuture = NaiveFuture.Null }

        interface IFuture<unit> with
            member this.Poll(context) =
                let rec loop (this: IterAsync<'a>) (context: IContext) : Poll<unit> =
                    if this.ActionFuture.IsNull then
                        let pollNext = this.Source.PollNext(context)
                        match pollNext with
                        | NaivePollNext.Pending ->
                            Poll.Pending
                        | NaivePollNext.Completed ->
                            Poll.Ready ()
                        | NaivePollNext.Next value ->
                            let activity = this.Action value
                            this.ActionFuture <- NaiveFuture(activity)
                            loop this context
                    else
                        let poll = this.ActionFuture.Poll(context)
                        match poll with
                        | NaivePoll.Pending ->
                            Poll.Pending
                        | NaivePoll.Ready () ->
                            this.ActionFuture.SetNull()
                            loop this context
                loop this context

            member this.Drop() =
                if not this.ActionFuture.IsNull then
                    this.ActionFuture.Drop()
                this.Source.Drop()

    [<Sealed>]
    type FoldStream<'s, 'a> =

        val private Folder: 's -> 'a -> IFuture<'s>
        val mutable private State: 's
        val private Source: NaiveStream<'a>
        val mutable private FolderFuture: NaiveFuture<'s>

        new(folder: 's -> 'a -> IFuture<'s>, initialState: 's, source: IStream<'a>) =
            { Folder = folder
              State = initialState
              Source = NaiveStream(source)
              FolderFuture = NaiveFuture.Null }

        interface IFuture<'s> with
            member this.Poll(context) =
                let rec loop (this: FoldStream<'s, 'a>) (context: IContext) : Poll<'s> =
                    if this.FolderFuture.IsNull then
                        // 'awaitNext
                        let pollNext = this.Source.PollNext(context)
                        match pollNext with
                        | NaivePollNext.Pending ->
                            Poll.Pending
                        | NaivePollNext.Completed ->
                            Poll.Ready this.State
                        | NaivePollNext.Next value ->
                            let folderFuture = this.Folder this.State value
                            this.FolderFuture <- NaiveFuture(folderFuture)
                            // GOTO: 'foldElement
                            loop this context
                    else
                        // 'foldElement
                        let poll = this.FolderFuture.Poll(context)
                        match poll with
                        | NaivePoll.Pending ->
                            Poll.Pending
                        | NaivePoll.Ready state ->
                            this.State <- state
                            loop this context
                loop this context

            member this.Drop() =
                if this.FolderFuture.IsNotNull then
                    this.FolderFuture.Drop()
                this.Source.Drop()

    [<Sealed>]
    type TakeFirst<'a> =
        val private Source: NaiveStream<'a>
        new(source: IStream<'a>) =
            { Source = NaiveStream(source) }

        interface IFuture<'a option> with
            member this.Poll(context) =
                let pollNext = this.Source.PollNext(context)
                match pollNext with
                | NaivePollNext.Pending -> Poll.Pending
                | NaivePollNext.Next value -> Poll.Ready (Some value)
                | NaivePollNext.Completed -> Poll.Ready None

            member this.Drop() =
                this.Source.Drop()


[<RequireQualifiedAccess>]
module Stream =

    // -----------
    // Creation
    // -----------

    let inline empty<'a> : Stream<'a> =
        Streams.Empty.Instance

    /// Always returns SeqNext of the value
    let inline always (value: 'a) : Stream<'a> =
        Streams.Always(value)

    let inline never<'a> : Stream<'a> =
        Streams.Never.Instance

    let inline single (value: 'a) : Stream<'a> =
        Streams.Seq([value])

    let inline replicate (count: int) (value: 'a) : Stream<'a> =
        Streams.Seq(Seq.replicate count value)

    let inline init (count: int) (initializer: int -> 'a) : Stream<'a> =
        Streams.Seq(Seq.init count initializer)

    let inline initInfinite (initializer: int -> 'a) : Stream<'a> =
        Streams.Seq(Seq.initInfinite initializer)

    let inline ofSeq (source: 'a seq) : Stream<'a> =
        Streams.Seq(source)

    // -----------
    // Combinators
    // -----------

    let inline map (mapping: 'a -> 'b) (source: Stream<'a>) : Stream<'b> =
        Streams.Map(mapping, source)

    let inline collect (collector: 'a -> Stream<'b>) (source: Stream<'a>) : Stream<'b> =
        Streams.Bind(collector, source)

    /// Alias to `collect` function
    let inline bind binder source =
        collect binder source

    let inline iterBlocking (action: 'a -> unit) (source: Stream<'a>) : IFuture<unit> =
        let action value =
            do action value
            Future.unit'
        Futures.IterAsync(action, source)

    let inline iter (action: 'a -> IFuture<unit>) (source: Stream<'a>) : IFuture<unit> =
        Futures.IterAsync(action, source)

    let inline foldBlocking (folder: 's -> 'a -> 's) (initState: 's) (source: Stream<'a>): IFuture<'s> =
        let folder state value =
            let state = folder state value
            Future.ready state
        Futures.FoldStream(folder, initState, source)

    let inline fold (folder: 's -> 'a -> IFuture<'s>) (initState: 's) (source: Stream<'a>): IFuture<'s> =
        Futures.FoldStream(folder, initState, source)

    let join (source: Stream<Stream<'a>>) : Stream<'a> =
        bind id source

    let scan (folder: 's -> 'a -> 's) (initState: 's) (source: Stream<'a>) : Stream<'s> =
        let mutable state = initState
        let binder value =
            let state' = folder state value
            state <- state'
            Streams.SingleValue(state') :> IStream<_>
        Streams.Bind(id, Streams.Seq([ Streams.SingleValue(initState) :> IStream<_>; Streams.Bind(binder, source) ]))

    // let chooseV (chooser: 'a -> 'b voption) (source: Stream<'a>) : Stream<'b> =
    //     let binder value =
    //         match chooser value with
    //         | ValueNone -> Streams.Empty.Instance :> IStream<_>
    //         | ValueSome value -> Streams.SingleValue(value) :> IStream<_>
    //     Streams.Bind(binder, source)

    let choose (chooser: 'a -> 'b option) (source: Stream<'a>) : Stream<'b> =
        let binder value =
            match chooser value with
            | None -> Streams.Empty.Instance :> IStream<_>
            | Some value -> Streams.SingleValue(value) :> IStream<_>
        Streams.Bind(binder, source)

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
                | PollNext.Pending -> Poll.Pending
                | PollNext.Completed -> Poll.Ready ValueNone
                | PollNext.Next x ->
                    let r = chooser x
                    match r with
                    | ValueNone -> Poll.Pending
                    | ValueSome r ->
                        _result <- ValueSome r
                        _source <- Unchecked.defaultof<_>
                        Poll.Ready _result
        <| fun () ->
            source.Drop()

    let tryPick (chooser: 'a -> 'b option) (source: Stream<'a>) : IFuture<'b option> =
        tryPickV (chooser >> Option.toValueOption) source |> Future.map Option.ofValueOption

    let pickV (chooser: 'a -> 'b voption) (source: Stream<'a>) : IFuture<'b> =
        tryPickV chooser source
        |> Future.map ^function
            | ValueSome r -> r
            | ValueNone -> raise (System.Collections.Generic.KeyNotFoundException())

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
            else PollNext.Completed
        <| fun () ->
            dropNullable _source1
            dropNullable _source2

    let bufferByCount (bufferSize: int) (source: Stream<'a>) : Stream<'a[]> =
        let mutable buffer = Array.zeroCreate bufferSize
        let mutable currIdx = 0
        Stream.create
        <| fun context ->
            if obj.ReferenceEquals(buffer, null) then
                PollNext.Completed
            else
            let rec loop () =
                let p = source.PollNext(context)
                match p with
                | PollNext.Pending -> PollNext.Pending
                | PollNext.Completed ->
                    let result = buffer.[0..currIdx]
                    buffer <- null
                    PollNext.Next result
                | PollNext.Next x ->
                    if currIdx >= bufferSize then
                        currIdx <- 0
                        let buffer' = buffer
                        buffer <- Array.zeroCreate bufferSize
                        PollNext.Next buffer'
                    else
                        buffer.[currIdx] <- x
                        currIdx <- currIdx + 1
                        loop ()
            loop ()
        <| fun () ->
            source.Drop()
            buffer <- Unchecked.defaultof<_>

    let filter (predicate: 'a -> bool) (source: Stream<'a>) : Stream<'a> =
        Stream.create
        <| fun context ->
            let rec loop () =
                let sPoll = source.PollNext(context)
                match sPoll with
                | PollNext.Pending -> PollNext.Pending
                | PollNext.Completed -> PollNext.Completed
                | PollNext.Next x ->
                    if predicate x then
                        PollNext.Next x
                    else
                        loop ()
            loop ()
        <| fun () ->
            source.Drop()

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
                    | PollNext.Pending -> Poll.Pending
                    | PollNext.Completed ->
                        result <- ValueSome false
                        Poll.Ready false
                    | PollNext.Next x ->
                        if predicate x then
                            result <- ValueSome true
                            Poll.Ready true
                        else
                            loop ()
            loop ()
        <| source.Drop

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
                    | PollNext.Pending -> Poll.Pending
                    | PollNext.Completed ->
                        result <- ValueSome true
                        Poll.Ready true
                    | PollNext.Next x ->
                        if predicate x then
                            loop ()
                        else
                            result <- ValueSome false
                            Poll.Ready false
            loop ()
        <| fun () ->
            source.Drop()

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
            | PollNext.Completed, _ ->
                source2.Drop()
                PollNext.Completed
            | _, PollNext.Completed ->
                source1.Drop()
                PollNext.Completed
            | PollNext.Pending, _ ->
                v1 <- ValueNone
                PollNext.Pending
            | _, PollNext.Pending ->
                v2 <- ValueNone
                PollNext.Pending
            | PollNext.Next x1, PollNext.Next x2 ->
                v1 <- ValueNone
                v2 <- ValueNone
                PollNext.Next (x1, x2)

        <| fun () ->
            source1.Drop()
            source2.Drop()

    let tryHeadV (source: Stream<'a>) : IFuture<'a voption> =
        Future.create
        <| fun context ->
            match source.PollNext(context) with
            | PollNext.Pending -> Poll.Pending
            | PollNext.Completed -> Poll.Ready ValueNone
            | PollNext.Next x -> Poll.Ready (ValueSome x)
        <| source.Drop

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
                | PollNext.Pending -> Poll.Pending
                | PollNext.Completed -> Poll.Ready last
                | PollNext.Next x ->
                    last <- ValueSome x
                    loop ()
            loop ()
        <| fun () ->
            source.Drop()

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
                PollNext.Completed
            else
                let p = _fut.Poll(context)
                match p with
                | Poll.Pending -> PollNext.Pending
                | Poll.Ready x ->
                    _fut <- Unchecked.defaultof<_>
                    PollNext.Next x
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
                x.Drop()
                _inner <- ValueNone
            | ValueNone -> ()

    let take (count: int) (source: Stream<'a>) : Stream<'a> =
        let mutable _taken = 0
        Stream.create
        <| fun context ->
            if _taken >= count then
                PollNext.Completed
            else
            let p = source.PollNext(context)
            match p with
            | PollNext.Pending -> PollNext.Pending
            | PollNext.Completed -> PollNext.Completed
            | PollNext.Next x ->
                _taken <- _taken + 1
                if _taken >= count then
                    source.Drop()
                PollNext.Next x
        <| fun () ->
            source.Drop()
