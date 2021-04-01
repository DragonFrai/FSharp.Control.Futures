namespace FSharp.Control.Futures

open System

// -------------------
// FutureBuilder
// -------------------

type FutureBuilder() =

    member inline _.Return(x): Future<'a> = Future.ready x

    member inline _.Bind(x, f) = Future.bind f x

    member inline _.Zero(): Future<unit> = Future.unit

    member inline _.ReturnFrom(f: Future<'a>): Future<'a> = f

    member inline this.Combine(uf: Future<unit>, u2f: unit -> Future<_>) = this.Bind(uf, u2f)

    member inline _.MergeSources(x1, x2) = Future.merge x1 x2

    member inline _.Delay(f: unit -> Future<'a>) = f

    member inline _.For(source, body) = Future.Seq.iterAsync source body

    member this.While(cond: unit -> bool, body: unit -> Future<unit>): Future<unit> =
        let rec loop () =
            if cond ()
            then this.Combine(body (), loop)
            else Future.unit
        loop ()

    member inline _.Using(d: 'D, f: 'D -> Future<'r>) : Future<'r> when 'D :> IDisposable =
        let fr = lazy(f d)
        let mutable disposed = false
        Future.Core.create
        <| fun context ->
            let fr = fr.Value
            match Future.Core.poll context fr with
            | Poll.Ready x ->
                if not disposed && d <> null then
                    d.Dispose()
                    disposed <- true
                Poll.Ready x
            | p -> p
        <| fun () ->
            if fr.IsValueCreated then
                fr.Value.Cancel()

    member inline _.Run(u2f): Future<'a> = Future.delay u2f


[<AutoOpen>]
module BuilderImpl =
    let future = FutureBuilder()
