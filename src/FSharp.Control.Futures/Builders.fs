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

    let inline tryWith (body: unit -> IComputation<'a>) (handler: exn -> IComputation<'a>) : IComputation<'a> =
        let mutable _current = TryWithState.Empty
        Computation.create
        <| fun ctx ->
            let rec pollCurrent () =
                match _current with
                | TryWithState.Empty ->
                    _current <- TryWithState.Body (body ())
                    pollCurrent ()
                | TryWithState.Body body ->
                    try
                        Computation.poll ctx body
                    with exn ->
                        _current <- TryWithState.Handler (handler exn)
                        pollCurrent ()
                | TryWithState.Handler handler ->
                    Computation.poll ctx handler
                | TryWithState.Cancelled -> raise FutureCancelledException
            pollCurrent ()
        <| fun () ->
            match _current with
            | TryWithState.Empty ->
                _current <- TryWithState.Cancelled
            | TryWithState.Body body ->
                _current <- TryWithState.Cancelled
                Computation.cancelNullable body
            | TryWithState.Handler handler ->
                _current <- TryWithState.Cancelled
                Computation.cancelNullable handler
            | TryWithState.Cancelled -> do ()

type ComputationBuilder() =

    member inline _.Return(x): IComputation<'a> = Computation.ready x

    member inline _.Bind(x, f) = Computation.bind f x

    member inline _.Zero(): IComputation<unit> = Computation.unit

    member inline _.ReturnFrom(f: IComputation<'a>): IComputation<'a> = f

    member inline this.Combine(uf: IComputation<unit>, u2f: unit -> IComputation<_>) = this.Bind(uf, u2f)

    member inline _.MergeSources(x1, x2) = Computation.merge x1 x2

    member inline _.Delay(f: unit -> IComputation<'a>) = f

    member inline _.For(source, body) = Computation.Seq.iterAsync source body

    member inline this.While(cond: unit -> bool, body: unit -> IComputation<unit>): IComputation<unit> =
        let whileSeq = seq { while cond () do yield () }
        this.For(whileSeq, body)

    member _.TryWith(body, handler): IComputation<'a> =
        Internal.tryWith body handler

    member inline _.Run(u2f): IComputation<'a> = Computation.delay u2f


[<AutoOpen>]
module ComputationBuilderImpl =
    let computation = ComputationBuilder()


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
