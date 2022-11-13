namespace FSharp.Control.Futures.Sync

open FSharp.Control.Futures

// TODO

// type Mutex<'a>(init: 'a) =
//     let mutable value = init
//     let sync = obj()
//     let mutable hasLock = false
//
// type Mutex


//
//[<Sealed>]
//type Mutex<'a>(inner: 'a) =
//
//    // NoLock ; HasLock
//
//    let sync = obj()
//    let mutable hasLock = false // flag has locker or no
//    let mutable waiters: IVar<'a> list = []
//
//    member _.Lock(action: 'a -> IComputationTmp<unit>) : IComputationTmp<unit> =
//        // release lock
//        let release () =
//            lock sync <| fun () ->
//                match waiters with
//                | waiter :: tail ->
//                    IVar.put inner waiter
//                    waiters <- tail
//                | _ ->
//                    hasLock <- false
////
//        future {
//            return! lock sync <| fun () ->
//                if hasLock
//                then
//                    let waiter = IVar()
//                    waiters <- List.append waiters [waiter]
//                    waiter
//                    |> Future.bind action
//                    |> Future.finally' (Future.lazy' release)
//                else
//                    hasLock <- true
//                    Future.ready inner
//                    |> Future.bind action
//                    |> Future.finally' (Future.lazy' release)
//        }
//
//and private MutexWaiter<'a> =
//
//    class end
