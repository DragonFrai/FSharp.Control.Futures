namespace FSharp.Control.Futures

open System
open System.Runtime.CompilerServices

// -------------------
// FutureBuilder
// -------------------

type FutureBuilder() =

    member inline _.Return(x): Future<'a> = Future.ready x

    member inline _.Bind(x, f) = Future.bind f x

    member inline _.Zero(): Future<unit> = Future.unit ()

    member inline _.ReturnFrom(f: Future<'a>): Future<'a> = f

    member inline this.Combine(uf: Future<unit>, u2f: unit -> Future<_>) = this.Bind(uf, u2f)

    member inline _.MergeSources(x1, x2) = Future.merge x1 x2

    member inline _.Delay(f: unit -> Future<'a>) = f

    member inline _.Run(u2f): Future<'a> = Future.delay u2f

    member inline _.For(seq, f) = Future.iterFuture seq f

    member inline _.Using(d: 'D, f: 'D -> Future<'r>) : Future<'r> when 'D :> IDisposable =
        let fr = lazy(f d)
        let mutable disposed = false
        { new Future<'r> with
            member _.Poll(waker) =
                let fr = fr.Value
                match Future.Core.poll waker fr with
                | Ready x ->
                    if not disposed then d.Dispose()
                    Ready x
                | p -> p }


[<AutoOpen>]
module BuilderImpl =
    let future = FutureBuilder()
