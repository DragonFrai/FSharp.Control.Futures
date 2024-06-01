namespace FSharp.Control.Futures.Internals

open FSharp.Control.Futures


[<RequireQualifiedAccess>]
module Futures =

    [<Sealed>]
    type Ready<'a>(value: 'a) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Ready value
            member this.Drop() = ()

    [<Sealed>]
    type ReadyUnit internal () =
        static member Instance : ReadyUnit = ReadyUnit()
        interface Future<unit> with
            member this.Poll(_ctx) = Poll.Ready ()
            member this.Drop() = ()

    let ReadyUnit : ReadyUnit = ReadyUnit()

    [<Sealed>]
    type Never<'a> internal () =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Pending
            member this.Drop() = ()

    let Never<'a> : Never<'a> = Never()

    [<Sealed>]
    type Lazy<'a>(lazy': unit -> 'a) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Ready (lazy' ())
            member this.Drop() = ()

    [<Sealed>]
    type Delay<'a>(delay: unit -> Future<'a>) =
        interface Future<'a> with
            member this.Poll(_ctx) = Poll.Transit (delay ())
            member this.Drop() = ()

    [<Sealed>]
    type Yield() =
        let mutable isYielded = false
        interface Future<unit> with
            member this.Poll(ctx) =
                if isYielded
                then Poll.Ready ()
                else
                    isYielded <- true
                    ctx.Wake()
                    Poll.Pending
            member this.Drop() = ()

    [<Sealed>]
    type Bind<'a, 'b>(source: Future<'a>, binder: 'a -> Future<'b>) =
        let mutable poller = NaiveFuture(source)
        interface Future<'b> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready result -> Poll.Transit (binder result)
                | NaivePoll.Pending -> Poll.Pending

            member this.Drop() =
                poller.Drop()

    [<Sealed>]
    type Map<'a, 'b>(source: Future<'a>, mapping: 'a -> 'b) =
        let mutable poller = NaiveFuture(source)
        interface Future<'b> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready result -> Poll.Ready (mapping result)
                | NaivePoll.Pending -> Poll.Pending

            member this.Drop() =
                poller.Drop()

    [<Sealed>]
    type Ignore<'a>(source: Future<'a>) =
        let mutable poller = NaiveFuture(source)
        interface Future<unit> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready _ -> Poll.Ready ()
                | NaivePoll.Pending -> Poll.Pending

            member this.Drop() =
                poller.Drop()

    [<Sealed>]
    type Merge<'a, 'b>(fut1: Future<'a>, fut2: Future<'b>) =
        let mutable sMerge = PrimaryMerge(fut1, fut2)

        interface Future<'a * 'b> with
            member this.Poll(ctx) =
                match sMerge.Poll(ctx) with
                | NaivePoll.Ready (struct (a, b)) -> Poll.Ready (a, b)
                | NaivePoll.Pending -> Poll.Pending

            member this.Drop() =
                sMerge.Drop()

    [<Sealed>]
    type First<'a>(fut1: Future<'a>, fut2: Future<'a>) =
        let mutable poller1 = NaiveFuture(fut1)
        let mutable poller2 = NaiveFuture(fut2)

        interface Future<'a> with
            member _.Poll(ctx) =
                try
                    match poller1.Poll(ctx) with
                    | NaivePoll.Ready result ->
                        poller2.Drop()
                        Poll.Ready result
                    | NaivePoll.Pending ->
                        try
                            match poller2.Poll(ctx) with
                            | NaivePoll.Ready result ->
                                poller1.Drop()
                                Poll.Ready result
                            | NaivePoll.Pending ->
                                Poll.Pending
                        with ex ->
                            poller2.Terminate()
                            poller1.Drop()
                            raise ex
                with _ ->
                    poller1.Terminate()
                    poller2.Drop()
                    reraise ()

            member _.Drop() =
                poller1.Drop()
                poller2.Drop()

    [<Sealed>]
    type Join<'a>(source: Future<Future<'a>>) =
        let mutable poller = NaiveFuture(source)
        interface Future<'a> with
            member this.Poll(ctx) =
                match poller.Poll(ctx) with
                | NaivePoll.Ready innerFut -> Poll.Transit innerFut
                | NaivePoll.Pending -> Poll.Pending
            member this.Drop() =
                poller.Drop()

    [<Sealed>]
    type Apply<'a, 'b>(fut: Future<'a>, futFun: Future<'a -> 'b>) =
        let mutable sMerge = PrimaryMerge(fut, futFun)

        interface Future<'b> with
            member _.Poll(ctx) =
                match sMerge.Poll(ctx) with
                | NaivePoll.Ready (struct (x, f)) -> Poll.Ready (f x)
                | NaivePoll.Pending -> Poll.Pending

            member _.Drop() =
                sMerge.Drop()

    [<Sealed>]
    type TryWith<'a>(body: Future<'a>, handler: exn -> Future<'a>) =
        let mutable poller = NaiveFuture(body)
        let mutable handler = handler

        interface Future<'a> with
            member _.Poll(ctx) =
                try
                    match poller.Poll(ctx) with
                    | NaivePoll.Pending -> Poll.Pending
                    | NaivePoll.Ready x ->
                        handler <- nullObj
                        Poll.Ready x
                with ex ->
                    poller.Terminate()
                    let h = handler
                    handler <- nullObj
                    Poll.Transit (h ex)

            member _.Drop() =
                handler <- nullObj
                poller.Drop()

    // TODO: TryFinally<'a>
    // [<Sealed>]
    // type TryFinally<'a>(body: Future<'a>, finalizer: unit -> unit) =
