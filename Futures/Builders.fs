namespace Futures


type FutureBuilder() =
    
    member _.Return(x): Future<'a> = Future.single x
    
    member _.Bind(x: Future<'a>, f: 'a -> Future<'b>): Future<'b> = Future.bind f x

    member _.Zero(): Future<unit> = Future.single ()
    
    member _.ReturnFrom(f: Future<'a>): Future<'a> = f
    
    member _.Combine(u: Future<unit>, f: Future<'a>): Future<'a> = Future.bind (fun () -> f) u

    member _.Delay(f: unit -> Future<'a>): Future<'a> =
        let mutable cell = None
        Future.create ^fun waker ->
            match cell with
            | Some future -> Future.poll waker future
            | None ->
                let future = f ()
                cell <- Some future
                Future.poll waker future


[<AutoOpen>]
module FutureBuilderImpl =
    let future = FutureBuilder()