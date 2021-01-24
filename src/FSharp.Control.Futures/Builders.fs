namespace FSharp.Control.Futures

open System
open System.Runtime.CompilerServices





module BuilderFutures =

    type IMakeNotDelayed<'a, 'fa when 'fa :> IFuture<'a>> =
        inherit IFuture<'a>
        abstract member MakeNotDelayed: unit -> 'fa


    [<Struct; NoComparison; NoEquality>]
    type ReadyFuture<'a>(value: 'a) =
        interface IFuture<'a> with member this.Poll(_) = Ready value
        interface IMakeNotDelayed<'a, ReadyFuture<'a>> with member this.MakeNotDelayed() = this

    [<Struct; NoComparison; NoEquality>]
    type ZeroFuture(_u: unit) =
        interface IFuture<unit> with member this.Poll(_) = Ready ()
        interface IMakeNotDelayed<unit, ZeroFuture> with member this.MakeNotDelayed() = this

    [<Struct; NoComparison; NoEquality>]
    type LazyFuture<'a, 'fa when 'fa :> IFuture<'a>> =
        struct
            val func: unit -> 'fa
            val mutable future: 'fa voption
            new(func) = { func = func; future = ValueNone }
        end
        interface IFuture<'a> with
            member this.Poll(waker) =
                match this.future with
                | ValueNone ->
                    let future = this.func ()
                    this.future <- ValueSome future
                    future.Poll(waker)
                | ValueSome future -> future.Poll(waker)
        interface IMakeNotDelayed<'a, 'fa> with member this.MakeNotDelayed() = this.func ()

    [<Struct; NoComparison; NoEquality>]
    type ConditionalFuture<'a, 'ft, 'ff when 'ft :> IFuture<'a> and 'ff :> IFuture<'a>> =
        struct
            val condition: bool
            val futureIfTrue: 'ft
            val futureIfFalse: 'ff
            new(c, t, f) = { condition = c; futureIfTrue = t; futureIfFalse = f }
        end
        interface IFuture<'a> with
            member this.Poll(waker) =
                if this.condition
                then this.futureIfTrue.Poll(waker)
                else this.futureIfFalse.Poll(waker)
        interface IMakeNotDelayed<'a, ConditionalFuture<'a, 'ft, 'ff>> with member this.MakeNotDelayed() = this

    [<Struct; NoComparison; NoEquality>]
    type BindFuture<'a, 'b, 'fa, 'fb when 'fa :> IFuture<'a> and 'fb :> IFuture<'b>> =
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
        interface IMakeNotDelayed<'b, BindFuture<'a, 'b, 'fa, 'fb>> with member this.MakeNotDelayed() = this

    [<Struct; NoComparison; NoEquality>]
    type CombineFuture<'fu, 'fa, 'a when 'fu :> IFuture<unit> and 'fa :> IFuture<'a>>(fu: 'fu, fa: 'fa) =
        interface IFuture<'a> with
            member this.Poll(waker) =
                match fu.Poll(waker) with
                | Ready () ->
                    fa.Poll(waker)
                | Pending -> Pending
        interface IMakeNotDelayed<'a, CombineFuture<'fu, 'fa, 'a>> with member this.MakeNotDelayed() = this

open BuilderFutures

// -------------------------
// StateMachineFutureBuilder
// -------------------------

type FutureBuilder() =

    member inline _.Return(x: 'a) = ReadyFuture(x)

    member inline _.Zero() = ZeroFuture(())

    member inline _.Bind(x: 'fa when 'fa :> IFuture<'a>, f: 'a -> 'fb) =
        { BindFuture.FutureA = x
          BindFuture.FutureB = ValueNone
          BindFuture.Binder = f }

    member inline _.ReturnFrom(fut: #IFuture<'a>) = fut

    member inline _.Combine(u: 'fu, fut: 'fa) = CombineFuture(u, fut)

//    member inline _.MergeSources(x1, x2): Future<'a * 'b> = Future.merge x1 x2

    member inline _.Delay(f: unit -> 'fa) = LazyFuture(f)

    //member inline this.Run(fut: #IFuture<'a>): #IFuture<'a> = fut

    member inline _.Run<'a, 'fut, 'fnd when 'fut :> IFuture<'a> and 'fut :> IMakeNotDelayed<'a, 'fnd>>(fut: 'fut) =
        fut //LazyStateMachine(fut.MakeNotDelayed)


[<AutoOpen>]
module StateMachineFutureBuilderImpl =
    let future = FutureBuilder()

    let inline shadow (fut: #IFuture<'a>) = fut :> IFuture<'a>

    let inline futureIfElse c t f = ConditionalFuture(c, t, f)


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
module FutureBuilderImpl =
    let legacyfuture = LegacyFutureBuilder()
