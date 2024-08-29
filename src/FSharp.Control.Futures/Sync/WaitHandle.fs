namespace FSharp.Control.Futures.Sync

open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.LowLevel





// type internal WaitHandleWaiter =
//     inherit IntrusiveNode<WaitHandleWaiter>
//     val mutable primaryNotify: PrimaryNotify
//
// type WaitHandle =
//     val mutable internal state: int
//     val mutable internal lock: SpinLock
//     val mutable internal waiters: IntrusiveList<WaitHandleWaiter>
//
//     new() =
//         { state = 0
//           lock = SpinLock()
//           waiters = IntrusiveList.Create() }
//
//     member inline private this.IncNotifications(): unit =
//         Interlocked.
//
//     member this.Notify()






// type WaitHandle =
//     val mutable primary: PrimaryNotify
//     val autoReset

// type [<Sealed>] NotifyImpl() =
//     let primaryNotify = PrimaryNotify(false)
//
//     member inline this.Notify() : unit =
//         //
//         ()
//
//     member inline this.RawPoll(ctx: IContext) : bool =
//
//         false
//
//     interface Future<unit> with
//         member this.Poll(ctx) : Poll<unit> =
//             ()
//
//
//
//
// /// Наипростейший примитив синхронизации, позволяющий асинхронно получать уведомление без значения
// /// Но только для одного получателя и только один раз
// and [<Struct; NoComparison; NoEquality>] Notify internal (impl: NotifyImpl) =
//     struct end
// and [<Struct; NoComparison; NoEquality>] NotifyHandle internal (impl: NotifyImpl) =
//     struct end
