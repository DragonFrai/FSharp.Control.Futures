module FSharp.Control.Futures.Sync.Notify

open System
open System.Collections.Generic
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Core


//[<Class; Sealed>]
//type Waiter() =
//    let mutable notified: bool = false
//    let mutable context: Context = null
//
//    interface IAsyncComputation<unit> with
//        member this.Poll(context) = failwith "todo"
//        member this.Cancel() = failwith "todo"



//module private NotifiedState =
//    [<Literal>]
//    let Empty = 0
//    [<Literal>]
//    let Waiting = 1 // has context
//    [<Literal>]
//    let Notified = 2
//    [<Literal>]
//    let Cancelled = 3
//
//module private NotifyState =
//    [<Literal>]
//    let Empty = 0
//    // Has waiters
//    [<Literal>]
//    let Waiting = 1
//    // NotifyOne was called without waiters
//    [<Literal>]
//    let Notified = 2
//
///// <summary> Notify a single task to wake up.
///// Notify provides a basic mechanism to notify a single task of an event.
///// Notify itself does not carry any data. Instead, it is to be used to signal another task to perform an operation.
///// </summary>
///// <remarks> If NotifyOne() is called before await Notified(), then the next await Notified() will complete immediately,
///// consuming the permit. Any subsequent awaiting to Notified() will wait for a new permit.
///// If NotifyOne() is called multiple times before await Notified(), only a single permit is stored.
///// The next await to Notified() will complete immediately, but the one after will wait for a new permit.
///// </remarks>
//[<Class>]
//type Notify() =
//    // TODO: make optimization
//
//    let syncObj = obj()
//    let mutable waiters = LinkedList<Notified>()
//    let mutable state = NotifyState.Empty
//
//    member inline private _.SyncObj = syncObj
//
//    member this.NotifyOne() =
//        lock syncObj (fun () ->
//            match state with
//            | NotifyState.Empty ->
//                state <- NotifyState.Notified
//            | NotifyState.Waiting ->
//                let toWake = waiters.First.Value
//                waiters.RemoveFirst()
//                if waiters.Count = 0 then state <- NotifyState.Empty
//                toWake.Notify()
//            | NotifyState.Notified -> ()
//            | _ -> invalidOp "unreachable match"
//        )
//
//    member this.NotifyWaiters() =
//        lock syncObj (fun () ->
//            match state with
//            | NotifyState.Empty -> ()
//            | NotifyState.Notified -> ()
//            | NotifyState.Waiting ->
//                for toWake in waiters do
//                    toWake.Notify()
//                waiters.Clear()
//                state <- NotifyState.Empty
//            | _ -> invalidOp "unreachable match"
//        )
//
//    member inline private this.AddNotified(notified: Notified) =
//        lock syncObj (fun () ->
//            match state with
//            | NotifyState.Empty -> ()
//            | NotifyState.Notified -> ()
//            | NotifyState.Waiting ->
//                for toWake in waiters do
//                    toWake.Notify()
//                waiters.Clear()
//            | _ -> invalidOp "unreachable match"
//        )
//
//    member inline private this.CheckNotifiedFlag(notified: Notified) =
//        lock syncObj (fun () ->
//            match state with
//            | NotifyState.Notified ->
//                state <- NotifyState.Empty
//                true
//            | NotifyState.Waiting | NotifyState.Empty->
//                waiters.AddLast(notified) |> ignore
//                state <- NotifyState.Waiting
//                false
//            | _ -> invalidOp "unreachable match"
//        )
//
//    interface Future<unit> with
//        member this.RunComputation() =
//            let waiter = Notified(this)
//            let node = lock syncObj (fun () -> waiters.AddLast(waiter))
//            waiter.Node <- node
//            Notified(this) :> IAsyncComputation<unit>
//
//
//
//and [<Sealed>]
//    Notified(notify: Notify) =
//        let mutable state = NotifiedState.Empty
//        let mutable context: Context = Unchecked.defaultof<_>
//        [<DefaultValue>]
//        val mutable Node: LinkedListNode<Notified>
//
//        member inline this.Notify() =
//            match state with
//            | NotifiedState.Notified -> ()
//            | NotifiedState.Cancelled -> ()
//            | NotifiedState.Empty ->
//                let current = Interlocked.CompareExchange(&state, NotifiedState.Notified, NotifiedState.Empty)
//                match current with
//                | NotifiedState.Waiting ->
//                    let current = Interlocked.CompareExchange(&state, NotifiedState.Notified, NotifiedState.Waiting)
//                    match current with
//                    | NotifiedState.Cancelled -> ()
//                    | NotifiedState.Notified ->
//                        context.Wake()
//                | NotifiedState.Notified -> ()
//                | NotifiedState.Cancelled -> ()
//            | NotifiedState.Waiting ->
//                let current = Interlocked.CompareExchange(&state, NotifiedState.Notified, NotifiedState.Waiting)
//                match current with
//                | NotifiedState.Cancelled -> ()
//                | NotifiedState.Notified ->
//                    context.Wake()
//
//        interface IAsyncComputation<unit> with
//            member this.Poll(ctx) =
//
//                failwith ""
//
//            member this.Cancel() =
//
//                failwith ""
//
//



