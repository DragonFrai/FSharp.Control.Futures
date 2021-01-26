namespace FSharp.Control.Futures

open System
open System.Runtime.CompilerServices



// -------------------
// LegacyFutureBuilder
// -------------------


[<Struct>]
type DelayState<'a> =
    | Function of func: (unit -> Future<'a>)
    | Future of fut: Future<'a>


[<Struct>]
type CombineState<'a> =
    | Step1 of step1: Future<unit> * Future<'a>
    | Step2 of step2: Future<'a>




type LegacyFutureBuilder() =

    member _.Return(x): Future<'a> = Future.ready x

    member _.Bind(x: Future<'a>, f: 'a -> Future<'b>): Future<'b> = Future.bind f x

    member _.Zero(): Future<unit> = Future.ready ()

    member _.ReturnFrom(f: Future<'a>): Future<'a> = f

    member _.Combine(u: Future<unit>, f: Future<'a>): Future<'a> =
        let mutable state = Step1(u, f)
        Future.create ^fun waker ->
            match state with
                | Step1 (fu, fa) ->
                    match Future.poll waker fu with
                    | Ready () ->
                        state <- Step2 fa
                        Future.poll waker fa
                    | Pending -> Pending
                | Step2 fa ->
                    Future.poll waker fa

    member _.MergeSources(x1, x2): Future<'a * 'b> = Future.merge x1 x2

    member _.Delay(f: unit -> Future<'a>): Future<'a> =
        let mutable state = DelayState.Function f
        Future.create ^fun waker ->
            match state with
            | Function f ->
                let fut = f ()
                state <- Future fut
                Future.poll waker fut
            | Future fut -> Future.poll waker fut

//    member _.Using(d: 'D, f: 'D -> Future<'r>) : Future<'r> when 'D :> IDisposable =
//        let fr = lazy(f d)
//        let mutable disposed = false
//        Future ^fun waker ->
//            let fr = fr.Value
//            match Future.poll waker fr with
//            | Ready x ->
//                if not disposed then d.Dispose()
//                Ready x
//            | p -> p



[<AutoOpen>]
module LegacyFutureBuilderImpl =
    let future = LegacyFutureBuilder()
