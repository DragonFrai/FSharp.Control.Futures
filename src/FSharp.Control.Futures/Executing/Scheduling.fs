namespace FSharp.Control.Futures.Scheduling
//
// open System.Threading
//
// open FSharp.Control.Futures
// open FSharp.Control.Futures.Internals
// open FSharp.Control.Futures.Scheduling.GlobalThreadPoolScheduler
// open FSharp.Control.Futures.Sync
//
//
// [<RequireQualifiedAccess>]
// module Schedulers =
//     let threadPool: IScheduler = GlobalThreadPoolScheduler.globalThreadPoolScheduler
//
//
// [<RequireQualifiedAccess>]
// module Scheduler =
//
//     /// <summary> Run Future on passed scheduler </summary>
//     /// <returns> Return Future waited result passed Future </returns>
//     let spawnOn (scheduler: IScheduler) (fut: Future<'a>) = scheduler.Spawn(fut)
//
//     /// <summary> Run Future on thread pool scheduler </summary>
//     /// <returns> Return Future waited result passed Future </returns>
//     let spawnOnThreadPool fut = spawnOn Schedulers.threadPool fut
