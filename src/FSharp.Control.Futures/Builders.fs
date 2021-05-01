namespace FSharp.Control.Futures

open System

// -------------------
// FutureBuilder
// -------------------

module private Internal =

    [<Struct; RequireQualifiedAccess>]
    type internal TryWithState<'body, 'handler> =
        | Empty
        | Body of body: 'body
        | Handler of handler: 'handler
        | Cancelled

    let inline tryWith (body: unit -> IAsyncComputation<'a>) (handler: exn -> IAsyncComputation<'a>) : IAsyncComputation<'a> =
        let mutable _current = TryWithState.Empty
        AsyncComputation.create
        <| fun ctx ->
            let rec pollCurrent () =
                match _current with
                | TryWithState.Empty ->
                    _current <- TryWithState.Body (body ())
                    pollCurrent ()
                | TryWithState.Body body ->
                    try
                        AsyncComputation.poll ctx body
                    with exn ->
                        _current <- TryWithState.Handler (handler exn)
                        pollCurrent ()
                | TryWithState.Handler handler ->
                    AsyncComputation.poll ctx handler
                | TryWithState.Cancelled -> raise FutureCancelledException
            pollCurrent ()
        <| fun () ->
            match _current with
            | TryWithState.Empty ->
                _current <- TryWithState.Cancelled
            | TryWithState.Body body ->
                _current <- TryWithState.Cancelled
                AsyncComputation.cancelNullable body
            | TryWithState.Handler handler ->
                _current <- TryWithState.Cancelled
                AsyncComputation.cancelNullable handler
            | TryWithState.Cancelled -> do ()

type AsyncComputationBuilder() =

    member inline _.Return(x): IAsyncComputation<'a> = AsyncComputation.ready x

    member inline _.Bind(x: IAsyncComputation<'a>, f: 'a -> IAsyncComputation<'b>) = AsyncComputation.bind f x

    member inline _.Zero(): IAsyncComputation<unit> = AsyncComputation.unit

    member inline _.ReturnFrom(f: IAsyncComputation<'a>): IAsyncComputation<'a> = f

    member inline this.Combine(uf: IAsyncComputation<unit>, u2f: unit -> IAsyncComputation<_>) = this.Bind(uf, u2f)

    member inline _.MergeSources(x1, x2) = AsyncComputation.merge x1 x2

    member inline _.Delay(f: unit -> IAsyncComputation<'a>) = f

    member inline _.For(source, body) = AsyncComputation.Seq.iterAsync source body

    member inline this.While(cond: unit -> bool, body: unit -> IAsyncComputation<unit>): IAsyncComputation<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

    member _.TryWith(body, handler): IAsyncComputation<'a> =
        Internal.tryWith body handler

    member inline _.Run(u2f): IAsyncComputation<'a> = AsyncComputation.delay u2f


[<AutoOpen>]
module ComputationBuilderImpl =
    let computation = AsyncComputationBuilder()


type FutureBuilder() =

    member inline _.Return(x: 'a): Future<'a> =
        Future.create (fun () -> computation.Return(x))

    member inline _.Bind(x, f) =
        Future.create (fun () -> computation.Bind(Future.run x, f >> Future.run))

    member inline _.Zero() = Future.unit

    member inline _.ReturnFrom(f: Future<'a>): Future<'a> = f

    member inline this.Combine(uf: Future<unit>, u2f: unit -> Future<_>) =
        Future.create (fun () -> computation.Combine(Future.run uf, u2f >> Future.run))

    member inline _.MergeSources(x1, x2) =
        Future.create (fun () -> computation.MergeSources(Future.run x1, Future.run x2))

    member inline _.Delay(f: unit -> Future<'a>) = f

    member inline _.For(source, body) = Future.create (fun () -> computation.For(source, body >> Future.run))

    member inline this.While(cond: unit -> bool, body: unit -> Future<unit>): Future<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

    member inline _.TryWith(body, handler): Future<'a> =
        Future.create (fun () -> computation.TryWith(body >> Future.run, handler >> Future.run))

    member inline _.Run(u2f): Future<'a> = Future.create (u2f >> Future.run)

[<AutoOpen>]
module FutureBuilderImpl =
    let future = FutureBuilder()
