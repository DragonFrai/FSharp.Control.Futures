namespace FSharp.Control.Futures

open System
open System.Runtime.CompilerServices


type FutureBuilder() =

    member _.Return(x): Future<'a> = Future.ready x

    member _.Bind(x: Future<'a>, f: 'a -> Future<'b>): Future<'b> = Future.bind f x

    member _.Zero(): Future<unit> = Future.ready ()

    member _.ReturnFrom(f: Future<'a>): Future<'a> = f

    member _.Combine(u: Future<unit>, f: Future<'a>): Future<'a> = Future.bind (fun () -> f) u

    member _.MergeSources(x1, x2): Future<'a * 'b> = Future.merge x1 x2

    member _.Delay(f: unit -> Future<'a>): Future<'a> =
        let mutable cell = ValueNone
        Future ^fun waker ->
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
    let future = FutureBuilder()





[<AutoOpen>]
module Helpers =
    [<Struct; NoComparison; NoEquality>]
    type ValueChoice<'a, 'b> =
        | ValueChoice1Of2 of item1: 'a
        | ValueChoice2Of2 of item2: 'b

module StateMachines =

    type IStateMachine<'a> =
        abstract member MoveNext: waker: Waker -> Poll<'a>

    [<Struct; NoComparison; NoEquality>]
    type ReadyStateMachine<'a> =
        { Value: 'a }
        interface IStateMachine<'a> with member this.MoveNext(_) = Ready this.Value

    [<Struct; NoComparison; NoEquality>]
    type LazyStateMachine<'a> =
        { mutable State: ValueChoice<unit -> 'a, 'a> }
        interface IStateMachine<'a> with
            member this.MoveNext(_) =
                match this.State with
                | ValueChoice1Of2 f ->
                    let value = f ()
                    this.State <- ValueChoice2Of2 value
                    Ready value
                | ValueChoice2Of2 value -> Ready value

    [<Struct; NoComparison; NoEquality>]
    type FutureStateMachine<'a> =
        { Future: Future<'a> }
        interface IStateMachine<'a> with member this.MoveNext(waker) = Future.poll waker this.Future

    [<Struct; NoComparison; NoEquality>]
    type ConditionalStateMachine<'a, 'ft, 'ff
        when 'ft :> IStateMachine<'a> and 'ft : struct
        and 'ff :> IStateMachine<'a> and 'ff : struct >
        =
        { Condition: bool
          IfTrue: 'ft
          IfFalse: 'ff }
        interface IStateMachine<'a> with
            member this.MoveNext(waker) =
                if this.Condition
                then this.IfTrue.MoveNext(waker)
                else this.IfFalse.MoveNext(waker)

    [<Struct; NoComparison; NoEquality>]
    type BindStateMachine<'a, 'b, 'fa, 'fb when
        'fa :> IStateMachine<'a> and 'fa : struct and
        'fb :> IStateMachine<'b> and 'fb : struct >
        =
        { FutureA: 'fa
          mutable FutureB: 'fb voption
          Binder: 'a -> 'fb }
        interface IStateMachine<'b> with
            member this.MoveNext(waker) =
                match this.FutureB with
                | ValueNone ->
                    let mutable fa = this.FutureA
                    let pa = fa.MoveNext(waker)
                    match pa with
                    | Ready va ->
                        let mutable fb = this.Binder va
                        this.FutureB <- ValueSome fb
                        fb.MoveNext(waker)
                    | Pending -> Pending
                | ValueSome fb ->
                    let mutable fb = fb
                    fb.MoveNext(waker)

open StateMachines

// -------------------------
// StateMachineFutureBuilder
// -------------------------

type StateMachineFutureBuilder() =

    member inline _.Return(x: 'a) = { ReadyStateMachine.Value = x }

    member inline this.Zero() = this.Return(())

    member inline _.Bind(x: 'fa, f: 'a -> 'fb) =
        { BindStateMachine.FutureA = x
          BindStateMachine.FutureB = ValueNone
          BindStateMachine.Binder = f }

    member inline this.Bind(x: Future<'a>, f: 'a -> 'fb) =
        { BindStateMachine.FutureA = { FutureStateMachine.Future = x }
          BindStateMachine.FutureB = ValueNone
          BindStateMachine.Binder = f }

    member inline _.ReturnFrom(sm: #IStateMachine<'a>) = sm

    member inline _.ReturnFrom(x: Future<'a>) = { FutureStateMachine.Future = x }

    member inline this.Combine(u: 'fu, x: 'fa) =
        this.Bind(u, fun () -> x)

//    member inline _.MergeSources(x1, x2): Future<'a * 'b> = Future.merge x1 x2

    member inline this.Delay(f: unit -> 'a) =
        this.Bind(this.Zero(), f)

    member inline this.Run(sm: #IStateMachine<'a>): Future<'a> =
        let mutable sm = sm
        Future ^fun waker ->
            sm.MoveNext(waker)


// --------------------------
// RawStateMachineFutureBuilder
// --------------------------
type RawStateMachineFutureBuilder() =

    member inline _.Return(x: 'a) = { ReadyStateMachine.Value = x }

    member inline this.Zero() = this.Return(())

    member inline _.Bind(x: 'fa, f: 'a -> 'fb) =
        { BindStateMachine.FutureA = x
          BindStateMachine.FutureB = ValueNone
          BindStateMachine.Binder = f }

    member inline this.Bind(x: Future<'a>, f: 'a -> 'fb) =
        { BindStateMachine.FutureA = { FutureStateMachine.Future = x }
          BindStateMachine.FutureB = ValueNone
          BindStateMachine.Binder = f }

    member inline _.ReturnFrom(sm: #IStateMachine<'a>) = sm

    member inline _.ReturnFrom(x: Future<'a>) = { FutureStateMachine.Future = x }

    member inline this.Combine(u: 'fu, x: 'fa) =
        this.Bind(u, fun () -> x)

    member inline this.Delay(f: unit -> 'a) =
        this.Bind(this.Zero(), f)

    member inline this.Run(sm: #IStateMachine<'a>) = sm



[<AutoOpen>]
module StateMachineFutureBuilderImpl =
    let smfuture = StateMachineFutureBuilder()

    let rawsmfuture = RawStateMachineFutureBuilder()

    let inline futIfElse (cond: bool) (ifTrue: 'ft) (ifFalse: 'ff) =
        { ConditionalStateMachine.Condition = cond
          ConditionalStateMachine.IfTrue = ifTrue
          ConditionalStateMachine.IfFalse = ifFalse }

    let inline asFuture (sm: #IStateMachine<'a>) =
        let mutable sm = sm
        Future ^fun waker ->
            sm.MoveNext(waker)
