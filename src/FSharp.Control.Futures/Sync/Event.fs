namespace FSharp.Control.Futures.Sync

open System.Diagnostics
open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel


type internal EventState =
    static member inline Unset: uint32 = 0u
    static member inline Set: uint32 = 1u
    static member inline IsSet(state: uint32): bool = state <> 0u
    static member inline IsUnset(state: uint32): bool = state = 0u

// TODO?: Add Reset instead realloc
// TODO?: Add registration listeners ?
// TODO?: Creating+Dropping EventWaiter in loop not efficient, we can add cloneable EventHandle that can be called only in one async context in loop and reuse Future creating.
[<Class>]
[<Sealed>]
type Event =
    val mutable internal waiters: IntrusiveList<EventWaiter>
    val internal syncObj: obj
    val mutable internal state: uint32

    new(isSet: bool) =
        let state = if isSet then EventState.Set else EventState.Unset
        { state = state; syncObj = obj(); waiters = IntrusiveList.Create() }

    new() =
        Event(false)

    member this.IsSet: bool =
        EventState.IsSet(this.state)

    member this.IsUnset: bool =
        EventState.IsUnset(this.state)

    member this.Set(): unit =
        let rec loop (this: Event) (state: uint32) =
            if EventState.IsUnset(state) then
                let prev = Interlocked.CompareExchange(&this.state, EventState.Set, state)
                if state = prev then
                    let toNotify = lock this.syncObj (fun () ->
                        this.waiters.Drain()
                    )
                    do IntrusiveNode.forEach (fun (waiter: EventWaiter) -> waiter.Notify()) toNotify
                    ()
                else
                    loop this prev
        loop this this.state

    member this.Wait(): Future<unit> =
        EventWaiter(this) :> Future<unit>


and [<Class>] EventWaiter =
    inherit IntrusiveNode<EventWaiter>

    val event: Event
    val mutable notify: PrimaryNotify

    new(event: Event) =
        { event = event; notify = PrimaryNotify.Create() }

    member internal this.Notify(): unit =
        do this.notify.Notify() |> ignore

    interface IFuture<unit> with
        member this.Poll(context) =
            if this.notify.IsInitOnly then
                let state = this.event.state
                if EventState.IsSet(state) then
                    Poll.Ready ()
                else
                    let isNotified = this.notify.Poll(context)
                    do Trace.Assert((isNotified = false))
                    let isSet =
                        lock this.event.syncObj (fun () ->
                            let state = this.event.state
                            if EventState.IsSet(state) then
                                do this.notify.Drop() |> ignore
                                true
                            else
                                this.event.waiters.PushBack(this)
                                false
                        )
                    if isSet
                    then Poll.Ready ()
                    else Poll.Pending
            else
                let isNotified = this.notify.Poll(context)
                if isNotified
                then Poll.Ready ()
                else Poll.Pending

        member this.Drop() =
            if this.notify.IsInitOnly then
                do this.notify.Drop() |> ignore
            else
                lock this.event.syncObj (fun () ->
                    let isRemoved = this.event.waiters.Remove(this)
                    Trace.Assert(isRemoved)
                )
                do this.notify.Drop() |> ignore
