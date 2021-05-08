module FSharp.Control.Futures.Sync.Notify

open System
open System.Collections.Generic
open System.Threading
open FSharp.Control.Futures


module private NotifiedState =
    [<Literal>]
    let Empty = 0
    [<Literal>]
    let Waiting = 1 // has context
    [<Literal>]
    let Notified = 2
    [<Literal>]
    let Cancelled = 3

[<Class>]
type Notify() =
    let syncObj = obj()
    let mutable waiters = LinkedList<Notified>()

    member inline private _.SyncObj = syncObj

    member this.NotifyAll() =
        let toWakeup =
            lock syncObj (fun () ->
                let tmp = waiters
                waiters <- LinkedList<Notified>()
                waiters)
        for waiter in toWakeup do
            failwith "unimpl"

    interface Future<unit> with
        member this.RunComputation() =
            let waiter = Notified(this)
            let node = lock syncObj (fun () -> waiters.AddLast(waiter))
            waiter.Node <- node
            Notified(this) :> IAsyncComputation<unit>



and [<Sealed>]
    Notified(notify: Notify) =
        let mutable state = NotifiedState.Empty
        let mutable context: Context = Unchecked.defaultof<_>
        [<DefaultValue>]
        val mutable Node: LinkedListNode<Notified>

        member inline this.Notify() =
            match state with
            | NotifiedState.Notified -> ()
            | NotifiedState.Cancelled -> ()
            | NotifiedState.Empty ->
                let current = Interlocked.CompareExchange(&state, NotifiedState.Notified, NotifiedState.Empty)
                match current with
                | NotifiedState.Waiting ->
                    let current = Interlocked.CompareExchange(&state, NotifiedState.Notified, NotifiedState.Waiting)
                    match current with
                    | NotifiedState.Cancelled -> ()
                    | NotifiedState.Notified ->
                        context.Wake()
                | NotifiedState.Notified -> ()
                | NotifiedState.Cancelled -> ()
            | NotifiedState.Waiting ->
                let current = Interlocked.CompareExchange(&state, NotifiedState.Notified, NotifiedState.Waiting)
                match current with
                | NotifiedState.Cancelled -> ()
                | NotifiedState.Notified ->
                    context.Wake()

        interface IAsyncComputation<unit> with
            member this.Poll(ctx) =

                failwith ""

            member this.Cancel() =

                failwith ""





