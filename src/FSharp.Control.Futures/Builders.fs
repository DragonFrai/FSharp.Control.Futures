namespace FSharp.Control.Futures

open System


type FutureBuilder() =

    member _.Return(x): Future<'a> = Future.ready x

    member _.Bind(x: Future<'a>, f: 'a -> Future<'b>): Future<'b> = Future.bind f x

    member _.Zero(): Future<unit> = Future.ready ()

    member _.ReturnFrom(f: Future<'a>): Future<'a> = f

    member _.Combine(u: Future<unit>, f: Future<'a>): Future<'a> = Future.bind (fun () -> f) u

    member _.MergeSources(x1, x2): Future<'a * 'b> = Future.merge x1 x2

    member _.Delay(f: unit -> Future<'a>): Future<'a> =
        let mutable cell = None
        Future ^fun waker ->
            match cell with
            | Some future -> Future.poll waker future
            | None ->
                let future = f ()
                cell <- Some future
                Future.poll waker future

    member _.Using(d: 'D, f: 'D -> Future<'r>) : Future<'r> when 'D :> IDisposable =
        let fr = lazy(f d)
        let mutable disposed = false
        Future ^fun waker ->
            let fr = fr.Value
            match Future.poll waker fr with
            | Ready x ->
                if not disposed then d.Dispose()
                Ready x
            | p -> p


[<AutoOpen>]
module FutureBuilderImpl =
    let future = FutureBuilder()
