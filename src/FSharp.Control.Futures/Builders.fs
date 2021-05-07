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

    member inline _.Bind(ca: IAsyncComputation<'a>, a2cb: 'a -> IAsyncComputation<'b>) = AsyncComputation.bind a2cb ca

    member inline _.Zero(): IAsyncComputation<unit> = AsyncComputation.unit

    member inline _.ReturnFrom(c: IAsyncComputation<'a>): IAsyncComputation<'a> = c

    member inline this.Combine(cu: IAsyncComputation<unit>, u2c: unit -> IAsyncComputation<'a>) = this.Bind(cu, u2c)

    member inline _.MergeSources(c1: IAsyncComputation<'a>, c2: IAsyncComputation<'b>): IAsyncComputation<'a * 'b> =
        AsyncComputation.merge c1 c2

    member inline _.Delay(u2c: unit -> IAsyncComputation<'a>) = u2c

    member inline _.For(source, body) = AsyncComputation.Seq.iterAsync source body

    member inline this.While(cond: unit -> bool, body: unit -> IAsyncComputation<unit>): IAsyncComputation<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

    member _.TryWith(body, handler): IAsyncComputation<'a> =
        Internal.tryWith body handler

    member inline _.Run(u2c: unit -> IAsyncComputation<'a>): unit -> IAsyncComputation<'a> = u2c


[<AutoOpen>]
module ComputationBuilderImpl =
    let computation = AsyncComputationBuilder()



type FutureBuilder() =

    member inline _.Return(x: 'a): Future<'a> =
        Future.create (fun () -> computation.Return(x))

    member inline _.Bind(x: Future<'a>, f: 'a -> Future<'b>) =
        Future.create (fun () -> computation.Bind(Future.runComputation x, f >> Future.runComputation))

    member inline _.Zero() = Future.create computation.Zero

    member inline _.ReturnFrom(f: Future<'a>): Future<'a> = f

    member inline this.Combine(uf: Future<unit>, u2f: unit -> Future<'a>) =
        Future.create (fun () -> computation.Combine(Future.runComputation uf, u2f >> Future.runComputation))

    member inline _.MergeSources(x1: Future<'a>, x2: Future<'b>) =
        Future.create (fun () -> computation.MergeSources(Future.runComputation x1, Future.runComputation x2))

    member inline _.Delay(f: unit -> Future<'a>) = f

    member inline _.For(source, body) = Future.create (fun () -> computation.For(source, body >> Future.runComputation))

    member inline this.While(cond: unit -> bool, body: unit -> Future<unit>): Future<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

    member inline _.TryWith(body, handler): Future<'a> =
        Future.create (fun () -> computation.TryWith(body >> Future.runComputation, handler >> Future.runComputation))

    member inline _.Run(u2f): Future<'a> = Future.create (u2f >> Future.runComputation)

[<AutoOpen>]
module FutureBuilderImpl =
    let future = FutureBuilder()
