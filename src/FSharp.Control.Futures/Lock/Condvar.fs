namespace FSharp.Control.Futures.Lock

open System.Threading
open FSharp.Control.Futures
open FSharp.Control.Futures.Internals

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
