namespace FSharp.Control.Futures

open System
open System.Runtime.CompilerServices





module StateMachines =

    [<Struct; NoComparison; NoEquality>]
    type ReadyStateMachine<'a>(value: 'a) =
        interface IFuture<'a> with member this.Poll(_) = Ready value

    [<Struct; NoComparison; NoEquality>]
    type ZeroStateMachine(_u: unit) =
        interface IFuture<unit> with member this.Poll(_) = Ready ()

    [<Struct; NoComparison; NoEquality>]
    type DelayStateMachine<'a, 'fa when 'fa :> IFuture<'a>> =
        val func: unit -> 'fa
        val mutable future: 'fa voption
        new(func) = { func = func; future = ValueNone }
        interface IFuture<'a> with
            member this.Poll(waker) =
                match this.future with
                | ValueNone ->
                    let future = this.func ()
                    this.future <- ValueSome future
                    future.Poll(waker)
                | ValueSome future -> future.Poll(waker)

    [<Struct; NoComparison; NoEquality>]
    type ConditionalStateMachine<'a, 'ft, 'ff when 'ft :> IFuture<'a> and 'ff :> IFuture<'a>> =
        val condition: bool
        val futureIfTrue: 'ft
        val futureIfFalse: 'ff
        new(c, t, f) = { condition = c; futureIfTrue = t; futureIfFalse = f }

        interface IFuture<'a> with
            member this.Poll(waker) =
                if this.condition
                then this.futureIfTrue.Poll(waker)
                else this.futureIfFalse.Poll(waker)

    [<Struct; NoComparison; NoEquality>]
    type BindStateMachine<'a, 'b, 'fa, 'fb when 'fa :> IFuture<'a> and 'fb :> IFuture<'b>> =
        { FutureA: 'fa
          mutable FutureB: 'fb voption
          Binder: 'a -> 'fb }
        interface IFuture<'b> with
            member this.Poll(waker) =
                match this.FutureB with
                | ValueNone ->
                    let mutable fa = this.FutureA
                    let pa = fa.Poll(waker)
                    match pa with
                    | Ready va ->
                        let mutable fb = this.Binder va
                        this.FutureB <- ValueSome fb
                        fb.Poll(waker)
                    | Pending -> Pending
                | ValueSome fb ->
                    let mutable fb = fb
                    fb.Poll(waker)

    [<Struct; NoComparison; NoEquality>]
    type CombineStateMachine<'fu, 'fa, 'a when 'fu :> IFuture<unit> and 'fa :> IFuture<'a>>(fu: 'fu, fa: 'fa) =
        interface IFuture<'a> with
            member this.Poll(waker) =
                match fu.Poll(waker) with
                | Ready () ->
                    fa.Poll(waker)
                | Pending -> Pending


open StateMachines

// -------------------------
// StateMachineFutureBuilder
// -------------------------

type StateMachineFutureBuilder() =

    member inline _.Return(x: 'a) = ReadyStateMachine(x)

    member inline _.Zero() = ZeroStateMachine(())

    member inline _.Bind(x: 'fa when 'fa :> IFuture<'a>, f: 'a -> 'fb) =
        { BindStateMachine.FutureA = x
          BindStateMachine.FutureB = ValueNone
          BindStateMachine.Binder = f }

    member inline _.ReturnFrom(fut: #IFuture<'a>) = fut

    member inline _.Combine(u: 'fu, fut: 'fa) = CombineStateMachine(u, fut)

//    member inline _.MergeSources(x1, x2): Future<'a * 'b> = Future.merge x1 x2

    member inline _.Delay(f: unit -> 'fa) = DelayStateMachine(f)

    //member inline this.Run(fut: #IFuture<'a>): #IFuture<'a> = fut

    member inline _.Run(fut: 'fut) = fut


[<AutoOpen>]
module StateMachineFutureBuilderImpl =
    let smfuture = StateMachineFutureBuilder()


type FutureBuilder() =
    member inline _.Return(x) = smfuture.Return(x)

    member inline _.Zero() = smfuture.Zero()

    member inline _.Bind(x, f) = smfuture.Bind(x, f)

    member inline _.ReturnFrom(fut) = smfuture.ReturnFrom(fut)

    member inline _.Combine(u, fut) = smfuture.Combine(u, fut)

//    member inline _.MergeSources(x1, x2): Future<'a * 'b> = Future.merge x1 x2

    member inline _.Delay(f) = smfuture.Delay(f)

    //member inline this.Run(fut: #IFuture<'a>): #IFuture<'a> = fut

    member inline _.Run(fut): IFuture<_> = fut

[<AutoOpen>]
module FutureBuilderImpl =
    let future = FutureBuilder()


// -------------------
// LegacyFutureBuilder
// -------------------

type LegacyFutureBuilder() =

    member _.Return(x): IFuture<'a> = Future.ready x

    member _.Bind(x: IFuture<'a>, f: 'a -> IFuture<'b>): IFuture<'b> = Future.bind f x

    member _.Zero(): IFuture<unit> = Future.ready ()

    member _.ReturnFrom(f: IFuture<'a>): IFuture<'a> = f

    member _.Combine(u: IFuture<unit>, f: IFuture<'a>): IFuture<'a> = Future.bind (fun () -> f) u

    member _.MergeSources(x1, x2): IFuture<'a * 'b> = Future.merge x1 x2

    member _.Delay(f: unit -> IFuture<'a>): IFuture<'a> =
        let mutable cell = ValueNone
        Future.create ^fun waker ->
            match cell with
            | ValueSome future -> Future.poll waker future
            | ValueNone ->
                let future = f ()
                cell <- ValueSome future
                Future.poll waker future

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
    let legacyfuture = LegacyFutureBuilder()
