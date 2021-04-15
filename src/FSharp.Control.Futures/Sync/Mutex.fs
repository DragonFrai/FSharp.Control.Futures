namespace FSharp.Control.Futures.Sync

open System.Threading
open FSharp.Control.Futures

// TODO


[<Sealed>]
type Mutex<'a>(inner: 'a) =

    // NoLock ; HasLock

    let sync = obj()
    let mutable hasLock = false // flag has locker or no
    let mutable waiters: IVar<'a> list = []

    member _.Lock(action: 'a -> Future<unit>) : Future<unit> =
//        // release lock
        let release () =
            lock sync <| fun () ->
                match waiters with
                | waiter :: tail ->
                    IVar.put inner waiter
                    waiters <- tail
                | _ ->
                    hasLock <- false
//
        future {
            return! lock sync <| fun () ->
                if hasLock
                then
                    let waiter = IVar()
                    waiters <- List.append waiters [waiter]
                    waiter
                    |> Future.bind action
                    |> Future.finally' (Future.lazy' release)
                else
                    hasLock <- true
                    Future.ready inner
                    |> Future.bind action
                    |> Future.finally' (Future.lazy' release)
        }


    /// <summary> Sync version of Lock.
    /// Try immediately take monitor and execute passed action outside the async context. </summary>
    /// <returns> 'true' if blocking is successful </returns>
    member _.TryLock(action: 'a -> unit) : bool =
        let canEnter = Monitor.TryEnter(sync)
        if canEnter then
            action inner
            Monitor.Exit(sync)
            true
        else
            false

and private MutexWaiter<'a> =

    class end
